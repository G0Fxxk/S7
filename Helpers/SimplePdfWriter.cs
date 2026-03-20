using System.Globalization;
using System.IO;
using System.Text;

namespace S7WpfApp.Helpers;

/// <summary>
/// 极简 PDF 生成器 — 支持多页、文本、分隔线、嵌入 PNG 图片
/// 无需任何第三方库，直接写入 PDF 1.4 格式
/// </summary>
public class SimplePdfWriter : IDisposable
{
    private readonly List<byte[]> _pageContents = new();   // 每页的内容流
    private MemoryStream _currentPageStream;
    private StreamWriter _currentWriter;
    private bool _disposed;
    private bool _saved;

    private readonly List<byte[]> _images = new();

    public double PageWidth { get; } = 595.28;   // A4 宽 (pt)
    public double PageHeight { get; } = 841.89;  // A4 高 (pt)

    /// <summary>
    /// 页面可用高度（留上下边距各 40pt）
    /// </summary>
    public double UsableHeight => PageHeight - 80;

    public SimplePdfWriter()
    {
        _currentPageStream = new MemoryStream();
        _currentWriter = new StreamWriter(_currentPageStream, Encoding.ASCII, leaveOpen: true);
    }

    /// <summary>
    /// 当前页索引（从 0 开始）
    /// </summary>
    public int CurrentPageIndex => _pageContents.Count;

    /// <summary>
    /// 结束当前页，开始新的一页。返回新页的起始 Y 坐标（通常 40pt 上边距）。
    /// </summary>
    public double NewPage()
    {
        // 提交当前页
        _currentWriter.Flush();
        _pageContents.Add(_currentPageStream.ToArray());
        _currentWriter.Dispose();
        _currentPageStream.Dispose();

        // 开始新页
        _currentPageStream = new MemoryStream();
        _currentWriter = new StreamWriter(_currentPageStream, Encoding.ASCII, leaveOpen: true);
        return 40; // 上边距
    }

    /// <summary>
    /// 绘制文本行
    /// </summary>
    public void DrawText(string text, double x, double y, double fontSize = 12,
        bool bold = false, int r = 0x33, int g = 0x33, int b = 0x33)
    {
        double py = PageHeight - y;
        _currentWriter.Write("BT\n");
        _currentWriter.Write($"/F{(bold ? 2 : 1)} {fontSize.ToString("F1", CultureInfo.InvariantCulture)} Tf\n");
        _currentWriter.Write($"{(r / 255.0).ToString("F3", CultureInfo.InvariantCulture)} " +
                        $"{(g / 255.0).ToString("F3", CultureInfo.InvariantCulture)} " +
                        $"{(b / 255.0).ToString("F3", CultureInfo.InvariantCulture)} rg\n");
        _currentWriter.Write($"{x.ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"{py.ToString("F2", CultureInfo.InvariantCulture)} Td\n");
        _currentWriter.Write($"({EscapePdfString(text)}) Tj\n");
        _currentWriter.Write("ET\n");
    }

    /// <summary>
    /// 绘制水平分隔线
    /// </summary>
    public void DrawLine(double x1, double y, double x2, double lineWidth = 0.5)
    {
        double py = PageHeight - y;
        _currentWriter.Write($"{lineWidth.ToString("F1", CultureInfo.InvariantCulture)} w\n");
        _currentWriter.Write("0.6 0.6 0.6 RG\n");
        _currentWriter.Write($"{x1.ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"{py.ToString("F2", CultureInfo.InvariantCulture)} m\n");
        _currentWriter.Write($"{x2.ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"{py.ToString("F2", CultureInfo.InvariantCulture)} l\nS\n");
    }

    /// <summary>
    /// 添加 PNG 图片并指定位置（在当前页上）
    /// </summary>
    public void DrawImage(byte[] pngData, double x, double y, double w, double h)
    {
        int imgIdx = _images.Count;
        _images.Add(pngData);

        double py = PageHeight - y - h;
        _currentWriter.Write("q\n");
        _currentWriter.Write($"{w.ToString("F2", CultureInfo.InvariantCulture)} 0 0 " +
                        $"{h.ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"{x.ToString("F2", CultureInfo.InvariantCulture)} " +
                        $"{py.ToString("F2", CultureInfo.InvariantCulture)} cm\n");
        _currentWriter.Write($"/Img{imgIdx} Do\n");
        _currentWriter.Write("Q\n");
    }

    /// <summary>
    /// 保存至文件（多页）
    /// </summary>
    public void Save(string filePath)
    {
        if (_saved) return;
        _saved = true;

        // 提交最后一页
        _currentWriter.Flush();
        _pageContents.Add(_currentPageStream.ToArray());

        int pageCount = _pageContents.Count;
        int imgCount = _images.Count;

        // 对象编号规划：
        // 1 = Catalog, 2 = Pages, 3 = Font
        // 4 .. 4+pageCount-1 = Page objects
        // 4+pageCount .. 4+2*pageCount-1 = Content streams
        // 4+2*pageCount .. end = Image XObjects
        int firstPageObj = 4;
        int firstContentObj = firstPageObj + pageCount;
        int firstImgObj = firstContentObj + pageCount;

        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fs);
        var offsets = new List<long>();

        // Header
        Write(writer, "%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");

        // Obj 1: Catalog
        offsets.Add(fs.Position);
        Write(writer, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // Obj 2: Pages
        offsets.Add(fs.Position);
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
            kids.Append($"{firstPageObj + i} 0 R ");
        Write(writer, $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

        // Obj 3: Font (Helvetica)
        offsets.Add(fs.Position);
        Write(writer, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        // 构建全局图片资源引用
        var imgResAll = new StringBuilder();
        for (int i = 0; i < imgCount; i++)
            imgResAll.Append($"/Img{i} {firstImgObj + i} 0 R ");

        // Page objects + Content streams
        for (int p = 0; p < pageCount; p++)
        {
            // Page object
            offsets.Add(fs.Position);
            int contentObjNum = firstContentObj + p;
            Write(writer, $"{firstPageObj + p} 0 obj\n<< /Type /Page /Parent 2 0 R " +
                $"/MediaBox [0 0 {PageWidth.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"{PageHeight.ToString("F2", CultureInfo.InvariantCulture)}] " +
                $"/Contents {contentObjNum} 0 R " +
                $"/Resources << /Font << /F1 3 0 R /F2 3 0 R >> " +
                $"/XObject << {imgResAll}>>" +
                $" >> >>\nendobj\n");
        }

        for (int p = 0; p < pageCount; p++)
        {
            // Content stream
            offsets.Add(fs.Position);
            int contentObjNum = firstContentObj + p;
            var contentBytes = _pageContents[p];
            Write(writer, $"{contentObjNum} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
            writer.Write(contentBytes);
            Write(writer, "\nendstream\nendobj\n");
        }

        // Image XObjects
        foreach (var imgData in _images)
        {
            offsets.Add(fs.Position);
            int objNum = offsets.Count;
            var (pixels, imgW, imgH) = DecodePngToRgb(imgData);
            Write(writer, $"{objNum} 0 obj\n<< /Type /XObject /Subtype /Image " +
                $"/Width {imgW} /Height {imgH} /ColorSpace /DeviceRGB " +
                $"/BitsPerComponent 8 /Length {pixels.Length} >>\nstream\n");
            writer.Write(pixels);
            Write(writer, "\nendstream\nendobj\n");
        }

        // XRef table
        long xrefPos = fs.Position;
        Write(writer, "xref\n");
        Write(writer, $"0 {offsets.Count + 1}\n");
        Write(writer, "0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write(writer, $"{offset:D10} 00000 n \n");

        // Trailer
        Write(writer, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\n");
        Write(writer, $"startxref\n{xrefPos}\n%%EOF\n");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _currentWriter.Dispose();
        _currentPageStream.Dispose();
    }

    private static void Write(BinaryWriter w, string s) => w.Write(Encoding.ASCII.GetBytes(s));

    private static string EscapePdfString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '(' || c == ')' || c == '\\')
                sb.Append('\\').Append(c);
            else if (c > 127)
                sb.Append('?');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 将 PNG 解码为 RGB 像素数组（简化实现，使用 WPF 解码器）
    /// </summary>
    private static (byte[] Pixels, int Width, int Height) DecodePngToRgb(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
            ms, System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            frame, System.Windows.Media.PixelFormats.Rgb24, null, 0);
        int stride = converted.PixelWidth * 3;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return (pixels, converted.PixelWidth, converted.PixelHeight);
    }
}
