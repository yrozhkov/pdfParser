namespace PdfParser.pdfObjects
{
    /// <summary>
    /// Shapes are defined by a series of lines and curves. 
    /// lineTo  draw from the current point  which can be set with moveTo to the specified point
    /// Line move defferiantiates between setting the current point or moving to next
    /// </summary>
    public class LineMove : Point
    {
        public bool IsMove { get; set; }
    }
}