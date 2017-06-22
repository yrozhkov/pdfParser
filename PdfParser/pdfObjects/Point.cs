using System;

namespace PdfParser.pdfObjects
{
    /// <summary>
    /// Point in coordinate space
    /// </summary>
    public class Point
    {
        double TOLERANCE = 0.00001;


        public Point()
        {
            
        }
        public Point(Point point)
        {
            X = point.X;
            Y = point.Y;
        }

        public Point(float x, float y)
        {
            X = x;
            Y = y;
        }
        public float X { get; set; }
        public float Y { get; set; }


        public bool IsEqual(Point point)
        {
            if (point == null)
            {
                return false;
            }
           
            if (Math.Abs(point.X - X) < TOLERANCE && Math.Abs(point.Y - Y) < TOLERANCE)
            {
                return true;
            }
            return false;
        }
    }
}