namespace PdfParser.pdfObjects
{
    /// <summary>
    ///     Line in coordinate space
    ///     represented by begining coordinates extended from Point and end coordinates
    /// </summary>
    public class Line : Point
    {
        public Line(Point beginPoint, Point endPoint) : base(beginPoint)
        {
            X1 = endPoint.X;
            Y1 = endPoint.Y;
        }

        public float X1 { get; set; }
        public float Y1 { get; set; }
    }
}