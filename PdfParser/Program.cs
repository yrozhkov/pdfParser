using System;
using System.IO;
using Mono.Options;

namespace PdfParser
{
    /// <summary>
    ///     Programm to extaract tabular data from pdf documents
    ///     Output is delimeter separated file or STDOUT
    /// 
    /// PDF format is collection of shapes placed on coordinate pane
    /// Our goal if to assemble all graphical shapes wich might represent cells and try to fit text inside them
    /// if we can find a pattern in placement representing table when we are in bussiness
    /// </summary>
    internal class Program
    {
        private static void Main(string[] args)
        {
            string fileName = null;
            string delimeter = null;
            string outputFileName = null;

            var showHelp = false;
            var options = new OptionSet
            {
                {"f|inputFile=", "pdf file to parse", f => fileName = f},
                {"d|delimeter=", "optional output file delimeter", d => delimeter = d},
                {"o|outputFile=", "optional output file name", o => outputFileName = o},
                {"h|help", "show this message and exit", h => showHelp = h != null}
            };


            try
            {
                // parse the command line
                options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write(AppDomain.CurrentDomain.FriendlyName + ":");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `greet --help' for more information.");
            }

            if (showHelp)
                ShowHelp(options);

            if (string.IsNullOrEmpty(fileName))
                ShowHelp(options);
            var ext = Path.GetExtension(fileName);
            if (ext != ".pdf")
            {
                Console.Write(AppDomain.CurrentDomain.FriendlyName + ": ");
                Console.WriteLine("Only pdf files supported");
                return;
            }

            if (string.IsNullOrEmpty(delimeter))
                delimeter = ",";


            var extractor = new TableExtractor();
            var text = extractor.ExtractText(fileName, delimeter);

            if (!string.IsNullOrEmpty(outputFileName))
            {
                if (File.Exists(outputFileName))
                    File.Delete(outputFileName);
                File.AppendAllText(outputFileName, text);
            }

            else
                Console.WriteLine(text);
        }

        private static void ShowHelp(OptionSet options)
        {
            Console.WriteLine("Usage: " + AppDomain.CurrentDomain.FriendlyName + " [OPTIONS]+");
            Console.WriteLine();
            Console.WriteLine("Options:");
            options.WriteOptionDescriptions(Console.Out);
        }
    }
}