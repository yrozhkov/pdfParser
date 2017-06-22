 

namespace PdfParser.ParserObjects
{
    /// <summary>
    /// Rectangle represented by fixating a point and specifying width and height
    /// </summary>
    public class Rectangle : Point
    {
        public float Width { get; set; }
        public float Height { get; set; }

        /// <summary>
        /// Checks if current rectangle intersect with input rectangle
        /// </summary>
        /// <param name="rec"></param>
        /// <returns></returns>
        public bool Intersects(Rectangle rec)
        {
            if (rec.Y > Y && rec.Y - rec.Height < Y && rec.X < X && rec.X + rec.Width > X)
            {
                return true;
            }
            if (rec.Y > Y && rec.Y - rec.Height < Y && rec.X < X + Width && rec.X + rec.Width > X + Width)
            {
                return true;
            }
            return false;
        }
    }
}