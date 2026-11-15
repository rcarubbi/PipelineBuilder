using ExcelReader.Core.Writer;
using ExcelReader.Core.Writer.Xls;
using ExcelReader.Core.Writer.Xlsx;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

internal sealed class ExcelOutputWriter : IAsyncDisposable
{
    private readonly XlsWorkbookWriter? _xls;
    private readonly XlsxWorkbookWriter? _xlsx;
    private readonly XlsSheetWriter? _xlsSheet;
    private readonly XlsxSheetWriter? _xlsxSheet;

    public ExcelOutputWriter(Stream stream, string path, string sheet, string blockId)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".xls":
                _xls = XlsWorkbookWriter.Create(stream, leaveOpen: true);
                _xlsSheet = _xls.AddSheet(sheet);
                break;
            case ".xlsx":
                _xlsx = XlsxWorkbookWriter.Create(stream, leaveOpen: true);
                _xlsxSheet = _xlsx.AddSheet(sheet);
                break;
            default: throw new PipelineDataException("Excel output must use .xls or .xlsx.", blockId);
        }
    }

    public void Write(IEnumerable<object?> values)
    {
        using IRowWriter row = _xlsSheet is not null ? _xlsSheet.StartRow() : _xlsxSheet!.StartRow();
        foreach (var value in values)
        {
            if (value is bool boolean) row.Write(boolean);
            else if (value is double number) row.Write(number);
            else if (value is long integer && integer is >= -999999999999999 and <= 999999999999999) row.Write((double)integer);
            else row.Write(ValueConversion.Format(value));
        }
    }

    public async Task Complete(CancellationToken ct)
    {
        if (_xlsSheet is not null) await _xlsSheet.DisposeAsync();
        if (_xlsxSheet is not null) await _xlsxSheet.DisposeAsync();
        if (_xls is not null) await _xls.EndAsync(ct);
        if (_xlsx is not null) await _xlsx.EndAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_xls is not null) await _xls.DisposeAsync();
        if (_xlsx is not null) await _xlsx.DisposeAsync();
    }
}
