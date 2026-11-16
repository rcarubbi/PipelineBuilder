using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

internal static class ApiInputReader
{
    private const int MaxPages = 100;
    private const int MaxRows = 100_000;
    private const int MaxResponseBytes = 10 * 1024 * 1024;
    private static readonly SemaphoreSlim RequestSlots = new(8);
    private static readonly ConcurrentDictionary<string, CircuitState> Circuits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectSafely
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private static async ValueTask<Stream> ConnectSafely(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || !IsAllowedHost(host) && addresses.Any(IsPrivate))
            throw new HttpRequestException("API destination resolves to a private or local network address.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException) throw;
            }
        }
        throw new HttpRequestException("Could not connect to API destination.", lastError);
    }

    private static bool IsAllowedHost(string host) =>
        (Environment.GetEnvironmentVariable("PipelineApi__AllowedHosts") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(host, StringComparer.OrdinalIgnoreCase);

    public static async Task<ApiSchemaDiscoveryRecord> Discover(ApiInputStep api, CancellationToken ct)
    {
        var uri = await SafeUri(api.Url, api.Id, ct);
        var token = await GetToken(api, ct);
        using var request = new HttpRequestMessage(new HttpMethod(api.Method), uri);
        if (api.Method == "POST") request.Content = new StringContent(api.Body ?? "", Encoding.UTF8, "application/json");
        if (api.Headers is not null)
            foreach (var header in api.Headers) request.Headers.TryAddWithoutValidation(header.Key, Secret(header.Value));
        ApplyAuthentication(request, api, token);
        using var response = await SendWithRetry(request, api.Method == "GET", ct);
        if (!response.IsSuccessStatusCode) throw new PipelineDataException($"API returned HTTP {(int)response.StatusCode}.", api.Id);
        var bytes = await ReadBounded(response.Content, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var format = DetectFormat(api.Format, contentType, bytes);
        var dataPaths = new HashSet<string>(StringComparer.Ordinal);
        List<Dictionary<string, string?>> sample = [];
        if (format == "xml")
        {
            var document = ParseXml(bytes, api.Id);
            CollectXmlPaths(document.Root, "", dataPaths);
            var dataPath = string.IsNullOrWhiteSpace(api.DataPath) ? dataPaths.Order(StringComparer.Ordinal).FirstOrDefault() ?? "" : api.DataPath;
            var data = string.IsNullOrWhiteSpace(dataPath) ? document.Root : SelectXml(document.Root, dataPath);
            var records = XmlRows(data).Take(100).ToArray();
            foreach (var record in records)
            {
                var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var attribute in record.Attributes()) fields["@" + attribute.Name.LocalName] = attribute.Value;
                foreach (var child in record.Elements()) fields[child.Name.LocalName] = child.Value;
                sample.Add(fields);
            }
        }
        else
        {
            JsonNode? document;
            try { document = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }); }
            catch (JsonException ex) { throw new PipelineDataException("API returned invalid or excessively nested JSON.", api.Id, ex); }
            CollectJsonPaths(document, "", dataPaths);
            var dataPath = string.IsNullOrWhiteSpace(api.DataPath) ? dataPaths.Order(StringComparer.Ordinal).FirstOrDefault() ?? "" : api.DataPath;
            var data = Navigate(document, dataPath);
            var records = FlattenRows(data).Take(100).ToArray();
            foreach (var record in records)
            {
                if (record is JsonObject obj)
                {
                    var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                    FlattenFields(obj, "", fields);
                    sample.Add(fields);
                }
                else sample.Add(new(StringComparer.Ordinal) { ["value"] = record is null ? null : Scalar(record) });
            }
        }
        var names = sample.SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal);
        var columns = names.Select(name => PipelineFiles.Infer(name, sample.Select(row => row.GetValueOrDefault(name)).ToArray())).ToArray();
        if (columns.Length is < 1 or > 200) throw new PipelineDataException("API sample needs 1 to 200 scalar columns.", api.Id);
        return new ApiSchemaDiscoveryRecord(columns, dataPaths.Order(StringComparer.Ordinal).ToArray());
    }

    private static void CollectJsonPaths(JsonNode? node, string path, HashSet<string> paths)
    {
        if (node is JsonArray array)
        {
            if (path.Length > 0) paths.Add(path);
            foreach (var item in array) CollectJsonPaths(item, path, paths);
        }
        else if (node is JsonObject obj)
        {
            foreach (var property in obj) CollectJsonPaths(property.Value,
                string.IsNullOrEmpty(path) ? property.Key : $"{path}.{property.Key}", paths);
        }
    }

    private static void CollectXmlPaths(XElement? element, string path, HashSet<string> paths)
    {
        if (element is null) return;
        var children = element.Elements().ToArray();
        foreach (var group in children.GroupBy(child => child.Name.LocalName, StringComparer.Ordinal))
        {
            var childPath = string.IsNullOrEmpty(path) ? group.Key : $"{path}/{group.Key}";
            if (group.Count() > 1 && !string.IsNullOrEmpty(path)) paths.Add(path);
            CollectXmlPaths(group.First(), childPath, paths);
        }
    }

    public static async IAsyncEnumerable<Dictionary<string, object?>> Read(ApiInputStep api, [EnumeratorCancellation] CancellationToken ct)
    {
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalTimeout.CancelAfter(TimeSpan.FromMinutes(10));
        ct = totalTimeout.Token;
        var uri = await SafeUri(api.Url, api.Id, ct);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = 0;
        var token = await GetToken(api, ct);

        for (var page = 0; page < MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(uri.AbsoluteUri)) throw new PipelineDataException("HATEOAS pagination cycle detected.", api.Id);
            using var request = new HttpRequestMessage(new HttpMethod(page == 0 ? api.Method : "GET"), uri);
            if (page == 0 && api.Method == "POST") request.Content = new StringContent(api.Body ?? "", Encoding.UTF8, "application/json");
            if (api.Headers is not null)
                foreach (var header in api.Headers) request.Headers.TryAddWithoutValidation(header.Key, Secret(header.Value));
            ApplyAuthentication(request, api, token);
            using var response = await SendWithRetry(request, api.Method == "GET" || page > 0, ct);
            if (!response.IsSuccessStatusCode) throw new PipelineDataException($"API returned HTTP {(int)response.StatusCode}.", api.Id);
            var bytes = await ReadBounded(response.Content, ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var format = DetectFormat(api.Format, contentType, bytes);
            Uri? next;
            IEnumerable<Dictionary<string, object?>> records;
            if (format == "xml")
            {
                var document = ParseXml(bytes, api.Id);
                records = XmlRecords(document, api);
                next = api.UseLinkHeader ? ParseLink(response.Headers, uri) : null;
                next ??= string.IsNullOrWhiteSpace(api.NextPath) ? null : XmlNext(document, api.NextPath, uri);
            }
            else
            {
                JsonNode? document;
                try { document = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }); }
                catch (JsonException ex) { throw new PipelineDataException("API returned invalid or excessively nested JSON.", api.Id, ex); }
                var data = Navigate(document, api.DataPath);
                records = JsonRecords(data, api);
                next = api.UseLinkHeader ? ParseLink(response.Headers, uri) : null;
                next ??= string.IsNullOrWhiteSpace(api.NextPath) ? null : JsonNext(document, api.NextPath, uri);
            }
            foreach (var row in records)
            {
                if (++rows > MaxRows) throw new PipelineDataException($"API exceeded the {MaxRows} row limit.", api.Id);
                yield return row;
            }
            if (next is null) yield break;
            var candidate = await SafeUri(next.AbsoluteUri, api.Id, ct);
            if (!SameOrigin(candidate, uri))
                throw new PipelineDataException("Pagination link changed origin; credentials were not forwarded.", api.Id);
            uri = candidate;
        }
        throw new PipelineDataException($"API exceeded the {MaxPages} page limit.", api.Id);
    }

    private static async Task<string?> GetToken(ApiInputStep api, CancellationToken ct)
    {
        if (api.Authentication != "oauth2") return null;
        var tokenUrl = await SafeUri(Secret(api.OAuthTokenUrl), api.Id, ct);
        var clientId = Secret(api.ClientId);
        var clientSecret = Secret(api.ClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new PipelineDataException("OAuth Client Credentials are missing.", api.Id);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        var scope = Secret(api.Scope);
        if (!string.IsNullOrWhiteSpace(scope)) form["scope"] = scope;
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await SendWithRetry(request, true, ct);
        if (!response.IsSuccessStatusCode) throw new PipelineDataException($"OAuth token endpoint returned HTTP {(int)response.StatusCode}.", api.Id);
        var bytes = await ReadBounded(response.Content, ct);
        try
        {
            var node = JsonNode.Parse(bytes);
            return node?["access_token"]?.GetValue<string>() is { Length: > 0 } value ? value
                : throw new PipelineDataException("OAuth response has no access_token.", api.Id);
        }
        catch (JsonException ex) { throw new PipelineDataException("OAuth token endpoint returned invalid JSON.", api.Id, ex); }
    }

    private static void ApplyAuthentication(HttpRequestMessage request, ApiInputStep api, string? token)
    {
        switch (api.Authentication)
        {
            case "basic":
                var username = Secret(api.Username);
                var password = Secret(api.Password);
                if (username is null || string.IsNullOrWhiteSpace(password)) throw new PipelineDataException("Basic authentication credentials are missing.", api.Id);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
                break;
            case "bearer":
                var bearer = Secret(api.Token);
                if (string.IsNullOrWhiteSpace(bearer)) throw new PipelineDataException("Bearer token is missing.", api.Id);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                break;
            case "oauth2":
                if (string.IsNullOrWhiteSpace(token)) throw new PipelineDataException("OAuth access token is missing.", api.Id);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
            case "apiKey":
                if (string.IsNullOrWhiteSpace(api.ApiKeyName)) throw new PipelineDataException("API key header name is required.", api.Id);
                request.Headers.TryAddWithoutValidation(api.ApiKeyName, Secret(api.ApiKey));
                break;
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetry(HttpRequestMessage request, bool retry, CancellationToken ct)
    {
        var host = request.RequestUri!.Host;
        var circuit = Circuits.GetOrAdd(host, static _ => new CircuitState());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            circuit.ThrowIfOpen();
            try
            {
                HttpContent? content = null;
                if (request.Content is not null)
                {
                    content = new StringContent(await request.Content.ReadAsStringAsync(ct), Encoding.UTF8);
                    content.Headers.Clear();
                    foreach (var header in request.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Content = content };
                foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
                await RequestSlots.WaitAsync(ct);
                HttpResponseMessage response;
                try { response = await Client.SendAsync(copy, HttpCompletionOption.ResponseHeadersRead, ct); }
                finally { RequestSlots.Release(); copy.Dispose(); }
                if (response.IsSuccessStatusCode) circuit.Succeeded();
                else if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) circuit.Failed();
                if (retry && attempt < 2 && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
                {
                    var retryAfter = response.Headers.RetryAfter;
                    var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date
                        ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(250 * (1 << attempt)));
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                    response.Dispose();
                    await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException)
            {
                circuit.Failed();
                if (!retry || attempt >= 2) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), ct);
            }
        }
        throw new HttpRequestException("Retry attempts exhausted.");
    }

    private sealed class CircuitState
    {
        private readonly Lock _sync = new();
        private int _failures;
        private DateTimeOffset _openUntil;
        public void ThrowIfOpen()
        {
            lock (_sync)
                if (_openUntil > DateTimeOffset.UtcNow) throw new PipelineDataException("API circuit is open after repeated failures.");
        }
        public void Failed()
        {
            lock (_sync)
            {
                if (++_failures >= 5) _openUntil = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
        public void Succeeded()
        {
            lock (_sync) { _failures = 0; _openUntil = default; }
        }
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) throw new PipelineDataException("API response exceeds 10 MiB.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > MaxResponseBytes) throw new PipelineDataException("API response exceeds 10 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    private static JsonNode? Navigate(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is JsonObject obj) node = obj[part];
            else if (node is JsonArray array && int.TryParse(part, out var index) && index >= 0 && index < array.Count) node = array[index];
            else return null;
        }
        return node;
    }

    private static IEnumerable<Dictionary<string, object?>> JsonRecords(JsonNode? node, ApiInputStep api)
    {
        var records = FlattenRows(node);
        foreach (var item in records)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in api.Schema)
            {
                var value = item is JsonValue && column.Name == "value" ? item : Navigate(item, column.Name);
                row[column.Name] = PipelineFiles.ConvertValue(value is null ? null : Scalar(value), column, null, 1, api.Id);
            }
            yield return row;
        }
    }

    private static IEnumerable<JsonNode?> FlattenRows(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array)
                foreach (var row in FlattenRows(child)) yield return row;
        }
        else yield return node;
    }

    private static void FlattenFields(JsonObject value, string prefix, Dictionary<string, string?> fields)
    {
        foreach (var property in value)
        {
            var name = string.IsNullOrEmpty(prefix) ? property.Key : $"{prefix}.{property.Key}";
            if (property.Value is JsonObject nested) FlattenFields(nested, name, fields);
            else if (property.Value is JsonValue or null) fields[name] = property.Value is null ? null : Scalar(property.Value);
        }
    }

    private static string Scalar(JsonNode value) => value is JsonValue scalar
        ? scalar.ToJsonString().Trim('"') : value.ToJsonString();

    private static string DetectFormat(string requested, string contentType, byte[] bytes)
    {
        if (requested != "auto") return requested;
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)) return "xml";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return "json";
        return Encoding.UTF8.GetString(bytes).TrimStart().StartsWith('<') ? "xml" : "json";
    }

    private static XDocument ParseXml(byte[] bytes, string blockId)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxResponseBytes });
            var document = XDocument.Load(reader);
            if (document.Root is not null)
            {
                var elements = new Stack<(XElement Element, int Depth)>();
                elements.Push((document.Root, 1));
                while (elements.TryPop(out var item))
                {
                    if (item.Depth > 64) throw new PipelineDataException("XML response exceeds the 64-level nesting limit.", blockId);
                    foreach (var child in item.Element.Elements()) elements.Push((child, item.Depth + 1));
                }
            }
            return document;
        }
        catch (XmlException ex) { throw new PipelineDataException("API returned invalid or unsafe XML.", blockId, ex); }
    }

    private static IEnumerable<Dictionary<string, object?>> XmlRecords(XDocument document, ApiInputStep api)
    {
        var element = string.IsNullOrWhiteSpace(api.DataPath) ? document.Root : SelectXml(document.Root, api.DataPath);
        var nodes = XmlRows(element);
        foreach (var item in nodes)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in api.Schema)
            {
                var field = XmlField(item, column.Name) ?? (column.Name == "value" ? item.Value : null);
                row[column.Name] = PipelineFiles.ConvertValue(field, column, null, 1, api.Id);
            }
            yield return row;
        }
    }

    private static IEnumerable<XElement> XmlRows(XElement? element)
    {
        if (element is null) return [];
        var children = element.Elements().ToArray();
        return children.Length > 0 && children.Select(child => child.Name).Distinct().Count() == 1 ? children : [element];
    }

    private static Uri? JsonNext(JsonNode? root, string path, Uri current)
    {
        var value = Navigate(root, path);
        if (value is null) return null;
        return Uri.TryCreate(current, Scalar(value), out var uri) ? uri : null;
    }
    private static Uri? XmlNext(XDocument root, string path, Uri current)
    {
        var node = SelectXml(root.Root, path);
        return Uri.TryCreate(current, node?.Value, out var uri) ? uri : null;
    }
    private static XElement? SelectXml(XElement? root, string path)
    {
        if (root is null) return null;
        var current = root;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && NameMatches(root, parts[0])) parts = parts[1..];
        foreach (var part in parts)
        {
            var name = part.Contains(':') ? part[(part.IndexOf(':') + 1)..] : part;
            current = current.Elements().FirstOrDefault(element => element.Name.LocalName == name);
            if (current is null) return null;
        }
        return current;
    }
    private static bool NameMatches(XElement element, string name) => element.Name.LocalName == (name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name);
    private static string? XmlField(XElement item, string path)
    {
        if (path.StartsWith('@')) return item.Attributes().FirstOrDefault(a => a.Name.LocalName == path[1..])?.Value;
        var element = SelectXml(item, path);
        return element?.Value;
    }
    private static Uri? ParseLink(HttpResponseHeaders headers, Uri current)
    {
        if (!headers.TryGetValues("Link", out var values)) return null;
        foreach (var part in values.SelectMany(v => v.Split(',')))
            if (part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase) && part.IndexOf('<') is var start && start >= 0 && part.IndexOf('>') is var end && end > start)
                return new Uri(current, part[(start + 1)..end]);
        return null;
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private static async Task<Uri> SafeUri(string? value, string blockId, CancellationToken ct)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0)
            throw new PipelineDataException("API URL must be a public HTTP or HTTPS destination without embedded credentials.", blockId);
        if (IsAllowedHost(uri.Host)) return uri;
        var addresses = IPAddress.TryParse(uri.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new PipelineDataException("API destination resolves to a private or local network address; configure PipelineApi__AllowedHosts to permit it explicitly.", blockId);
        return uri;
    }
    private static bool IsPrivate(IPAddress ip) => ip.IsIPv4MappedToIPv6 && IsPrivate(ip.MapToIPv4()) ||
        IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
        ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Any) ||
        ip.MapToIPv6().GetAddressBytes()[0] == 0xfc || ip.MapToIPv6().GetAddressBytes()[0] == 0xfd ||
        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (ip.GetAddressBytes()[0] is 0 or 10 or 127 or >= 224 ||
            ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254 ||
            ip.GetAddressBytes()[0] == 100 && ip.GetAddressBytes()[1] is >= 64 and <= 127 ||
            ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168 ||
            ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 0 && ip.GetAddressBytes()[2] == 0 ||
            ip.GetAddressBytes()[0] == 198 && ip.GetAddressBytes()[1] is 18 or 19 ||
            ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] is >= 16 and <= 31);
    private static string? Secret(string? reference)
    {
        if (reference is null) return null;
        if (reference.StartsWith("${secret.", StringComparison.Ordinal) && reference.EndsWith('}'))
            return Environment.GetEnvironmentVariable(reference[9..^1]) ?? throw new PipelineDataException("Referenced server secret is unavailable.");
        return reference;
    }
}
