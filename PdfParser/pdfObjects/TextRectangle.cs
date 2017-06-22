namespace PdfParser.pdfObjects
{
    /// <summary>
    /// Represents cell with text
    /// </summary>
    public class TextRectangle : Rectangle
    {
        public bool Assigned { get; set; }
        public string Text { get; set; }
    }
}