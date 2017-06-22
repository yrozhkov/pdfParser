using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using PdfParser.Strategies;

namespace PdfParser
{
    internal class TableExtractor
    {
        public string ExtractText(string fileName, string delimeter)
        {
             
            var str = new StringBuilder();


            using (var reader = new PdfReader(fileName))
            {
                var pageBreak = "";
                for (var page = 1; page <= reader.NumberOfPages; page++)
                {
                    var pageSize = reader.GetPageSize(page);


                    var locationTableExtractionStrategy = new LocationTableExtractionStrategy(reader.GetPageRotation(page), pageSize.Width,
                        pageSize.Height, delimeter);
                    var text = PdfTextExtractor.GetTextFromPage(reader, page, locationTableExtractionStrategy);
                    str.Append(pageBreak);
                    str.Append(text);
                    pageBreak = "[---PageBreak---]\r\n";
                }
            }
            var doc = str.ToString();
            return doc;
        }
    }
}