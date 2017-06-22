using System.Collections.Generic;

namespace PdfParser.pdfObjects
{
    internal class TextRectangleTemplate : Rectangle
    {
         
        public List<TextRectangle> Text { get; set; }
         
        public int HorizontalSpan { get; set; }
        public int VerticalSpan { get; set; }

        public bool Contains(TextRectangle textRectangle, float error)
        {
            var heightTolerance = textRectangle.Height * 0.2;
            if (textRectangle.Y <= Y + heightTolerance && textRectangle.Y - textRectangle.Height >= Y - Height - heightTolerance && textRectangle.X >= X - error &&
                textRectangle.X + textRectangle.Width <= X + Width + error)
            {
                return true;
            }
            return false;
        }
    }
}