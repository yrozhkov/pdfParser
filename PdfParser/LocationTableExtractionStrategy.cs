using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.awt.geom;
using iTextSharp.text.pdf.parser;
using Logging;
using MoreLinq;
using PdfParser.pdfObjects;
using Line = PdfParser.pdfObjects.Line;
using Point = PdfParser.pdfObjects.Point;

namespace PdfParser
{
    /// <summary>
    ///     Serches for all shapes in pdf documents along with text and detects tabular patterns
    /// </summary>
    public class LocationTableExtractionStrategy : LocationTextExtractionStrategy, IExtRenderListener
    {
        private static readonly ILogger _log = LogManager.GetLoggerForCallingClass();


        private readonly List<Point> _currentPoints = new List<Point>();
        private readonly string _delimeter;
        private readonly List<Line> _lines = new List<Line>();

        private readonly float _pageHeight;
        private readonly int _pageRotation;
        private readonly float _pageWidth;
        private readonly List<Rectangle> _rectangles = new List<Rectangle>();

        private readonly List<TextRectangle> _textRectangles = new List<TextRectangle>();
        private readonly double TOLERANCE = 0.000001;
        private float _charWidth;
        private Rectangle _currentRectangle;
        private TextRectangle _lastChunk;


        public LocationTableExtractionStrategy(int pageRotation, float pageWidth, float pageHeight, string delimeter)
        {
            _pageRotation = pageRotation;
            _pageHeight = pageHeight;
            _pageWidth = pageWidth;
            _delimeter = delimeter;
        }


        //TODO: Add logic to analyze lines as delimeters
        /// <summary>
        ///     Tries to construct a rectangle from current points
        /// </summary>
        /// <param name="ctm"></param>
        private void ConstructShape(Matrix ctm)
        {
            if (_currentPoints.Count > 0)
                if (((_currentPoints.Count == 5) && _currentPoints[0].IsEqual(_currentPoints[4])) ||
                    (_currentPoints.Count == 4))
                {
                    var point = new Point[3];
                    point[0] = Transform(_currentPoints[0], ctm);
                    point[1] = Transform(_currentPoints[1], ctm);
                    point[2] = Transform(_currentPoints[2], ctm);

                    var maxY = point.Max(x => x.Y);
                    var minY = point.Min(x => x.Y);
                    var maxX = point.Max(x => x.X);
                    var minX = point.Min(x => x.X);


                    _rectangles.Add(new Rectangle
                    {
                        X = minX,
                        Y = maxY,
                        Height = maxY - minY,
                        Width = maxX - minX
                    });
                    return;
                }


            if (_currentPoints.Count == 0)
                return;

            var points =
                _currentPoints.Select(currentPoint => Transform(currentPoint, ctm))
                    .DistinctBy(p => new {p.X, p.Y})
                    .ToList();

            //we have a rectangle
            if (points.Count == 4)
            {
                var sortedPoints = points.OrderBy(x => x.X).ThenByDescending(x => x.Y).ToList();
                _rectangles.Add(new Rectangle
                {
                    X = sortedPoints[1].X,
                    Y = sortedPoints[1].Y,
                    Height = Math.Abs(sortedPoints[1].Y - sortedPoints[0].Y),
                    Width = sortedPoints[2].X - sortedPoints[0].X
                });
            }

            if (_currentPoints.Count == 2)
            {
                var beginPoint = Transform(_currentPoints[0], ctm);
                var endPoint = Transform(_currentPoints[1], ctm);
                _lines.Add(new Line(beginPoint, endPoint));
            }
        }

        /// <summary>
        ///     Applies  Affine transformation to point
        /// </summary>
        /// <param name="point"></param>
        /// <param name="transormationMatrix"></param>
        /// <returns></returns>
        private Point Transform(Point point, Matrix transormationMatrix)
        {
            var transformMatrix = new AffineTransform(transormationMatrix[Matrix.I11], transormationMatrix[Matrix.I12],
                transormationMatrix[Matrix.I21], transormationMatrix[Matrix.I22],
                transormationMatrix[Matrix.I31], transormationMatrix[Matrix.I32]);

            var dst = new Point2D.Float();

            transformMatrix.Transform(new Point2D.Float(point.X, point.Y), dst);


            if (_pageRotation == 0)
                return new Point(dst.x, _pageHeight - dst.y);

            if (_pageRotation == 90)
                return new Point(dst.x, dst.y);

            throw new Exception("Current page rotation is not handled");
        }


        private string GetResultantTextInternal(List<TextRectangle> textChunks, List<Rectangle> rectangles)
        {
            var mostCommonCellHeight = rectangles.Count > 0
                ? rectangles.GroupBy(x => x.Height)
                    .OrderByDescending(grp => grp.Count())
                    .Select(grp => grp.Key)
                    .First()
                : 1;


            var minHeight = textChunks.Min(x => x.Height);
            var minWidth = textChunks.Min(x => x.Width);


            var overflowDelta =
                textChunks.GroupBy(x => x.Height)
                    .OrderByDescending(grp => grp.Count())
                    .Select(grp => grp.Key)
                    .First()*0.2f;


            var textDictionary = GetTextDictionary(textChunks, overflowDelta);
            var rectDictionary = rectangles.Count > 0
                ? GetRectanglesDictionary(rectangles, mostCommonCellHeight, minHeight, minWidth)
                : new Dictionary<float, List<Rectangle>>();


            //We did not find any shapes so just print  text as is
            if (rectDictionary.Count == 0)
                return GenerateText(textDictionary);

            //Not used yet
            //  var vlines = StrategyHelper.GetVerticalLines(_lines);
            //  var hlines = StrategyHelper.GetHorizontalLines(_lines);

            var template = ArrangeDictionariesIntoTemplates(rectDictionary, textDictionary);


            foreach (var yCoordKey in textDictionary.Keys)
            {
                var textRect = textDictionary[yCoordKey].Where(x => !x.Assigned).ToList();
                PlaceTextRectIntoTemplates(textRect, template, yCoordKey, overflowDelta);
            }
            var delimetedText = CreateDelimetdTextFromTemplates(template);
            return delimetedText;
        }


        /// <summary>
        ///     groups all text by lines
        /// </summary>
        /// <param name="textRectangles"></param>
        /// <param name="overflowDelta"></param>
        /// <returns></returns>
        public Dictionary<float, List<TextRectangle>> GetTextDictionary(List<TextRectangle> textRectangles,
            float overflowDelta)
        {
            var rectDictionary = new Dictionary<float, List<TextRectangle>>();
            var displaced = new HashSet<float>();


            textRectangles = textRectangles.OrderBy(x => x.Y).ToList();

            foreach (var rectangle in textRectangles)
            {
                var ycoord = rectangle.Y;
                var found = false;

                if (!rectDictionary.ContainsKey(rectangle.Y))
                {
                    foreach (var y in rectDictionary.Keys)
                        if (Math.Abs(rectangle.Y - y) < rectangle.Height + overflowDelta)
                        {
                            if (Math.Abs(ycoord - y) > TOLERANCE)
                                displaced.Add(y);
                            ycoord = y;
                            found = true;
                            break;
                        }

                    if (!found)
                        rectDictionary[rectangle.Y] = new List<TextRectangle>();
                }

                rectDictionary[ycoord].Add(rectangle);
            }

            //deal with text wich is not vertically aligned
            if (displaced.Count > 0)
                HandleDisplaced(displaced, rectDictionary);

            var keys = rectDictionary.Keys.ToList();

            foreach (var k in keys)
                rectDictionary[k] = rectDictionary[k].OrderBy(x => x.X).ToList();
            return rectDictionary;
        }

        /// <summary>
        ///     Handles lines wich not perfectly alligned either by widening the space or ajusting to wider y coordinate
        /// </summary>
        /// <param name="displacedCoordinates"></param>
        /// <param name="rectDictionary"></param>
        private void HandleDisplaced(HashSet<float> displacedCoordinates,
            Dictionary<float, List<TextRectangle>> rectDictionary)
        {
            foreach (var displacedYcoord in displacedCoordinates)
            {
                var textRectangles = rectDictionary[displacedYcoord];
                var heightDict = textRectangles.GroupBy(x => x.Height).ToDictionary(x => x.Key, x => x.ToList());
                var cnt = 0;
                float y = 0;
                float h = 0;

                foreach (var height in heightDict.Values)
                    if (height.Count > cnt)
                    {
                        cnt = height.Count;
                        y = height[0].Y;
                        h = height[0].Height;
                    }
                    else if (height.Count == cnt)
                    {
                        if (height[0].Height > h)
                        {
                            y = height[0].Y;
                            h = height[0].Height;
                        }
                    }

                //we have  a baseline
                if (Math.Abs(y - displacedYcoord) > TOLERANCE)
                {
                    rectDictionary[y] = rectDictionary[displacedYcoord];
                    rectDictionary.Remove(displacedYcoord);
                }
            }
        }

        /// <summary>
        ///     This function minimizes all rectanges from the list and tries to fix misaligned ones
        /// </summary>
        /// <param name="rectangles"></param>
        /// <param name="avgHeight"></param>
        /// <param name="minHeight"></param>
        /// <param name="minWidth"></param>
        /// <returns></returns>
        public Dictionary<float, List<Rectangle>> GetRectanglesDictionary(List<Rectangle> rectangles, float avgHeight,
            float minHeight, float minWidth)
        {
            var tempRectangleDictionary = new Dictionary<float, List<Rectangle>>();
            var rectDictionary = new Dictionary<float, List<Rectangle>>();


            foreach (var rectangle in rectangles)
            {
                if ((rectangle.Y < 0) || (rectangle.Width < minWidth) || (rectangle.Height < minHeight) ||
                    ((rectangle.Height > avgHeight*3) && rectangles.Any(r => r.Intersects(rectangle))))
                    continue;


                if (!tempRectangleDictionary.ContainsKey(rectangle.Y))
                    tempRectangleDictionary[rectangle.Y] = new List<Rectangle>();
                tempRectangleDictionary[rectangle.Y].Add(rectangle);
            }

            if (!tempRectangleDictionary.Any())
                return rectDictionary;

            var keys = tempRectangleDictionary.Keys.OrderBy(x => x).ToList();

            for (var k = 0; k < keys.Count; k++)
            {
                var key = keys[k];
                var cells =
                    tempRectangleDictionary[key].DistinctBy(x => new {x.X, W = x.Width}).OrderBy(x => x.X).ToList();
                var filteredList = cells.Where(x => (x.Width > 1) && (x.Height >= minHeight)).ToList();
                if (filteredList.Count > 1)
                {
                    rectDictionary[key] = filteredList;
                }
                else
                {
                    var currentCell = cells[0];
                    filteredList = new List<Rectangle>();

                    for (var i = 1; i < cells.Count; i++)
                    {
                        var width = cells[i].X - currentCell.X;
                        var height = currentCell.Height;
                        if (k > 0)
                            height = keys[k] - keys[k - 1];
                        if ((width > 1) && (height >= minHeight))
                            filteredList.Add(new Rectangle
                            {
                                Height = height,
                                Width = width,
                                X = currentCell.X,
                                Y = currentCell.Y
                            });
                        currentCell = cells[i];
                    }

                    if (filteredList.Count > 1)
                    {
                        if ((cells.Last().Width > 1) && (cells.Last().Height > 1))
                            filteredList.Add(new Rectangle
                            {
                                Height = filteredList.Last().Height,
                                Width = cells.Last().Width,
                                X = cells.Last().X,
                                Y = cells.Last().Y
                            });
                        rectDictionary[key] = filteredList;
                    }
                    if (cells.Count == 1)
                    {
                        filteredList.Add(cells[0]);
                        rectDictionary[key] = filteredList;
                    }
                }
            }


            return rectDictionary;
        }

        /// <summary>
        ///     Creates delimeted text from templates
        /// </summary>
        /// <param name="template"></param>
        /// <returns></returns>
        private string CreateDelimetdTextFromTemplates(Dictionary<float, List<TextRectangleTemplate>> template)
        {
            var delimetedText = new StringBuilder();
            var keys = template.Keys.OrderBy(x => x).ToList();
            foreach (var key in keys)
            {
                var templ = template[key].OrderBy(x => x.X).ToList();
                for (var index = 0; index < templ.Count; index++)
                {
                    var textRectangleTemplate = templ[index];
                    var tempTemplateDict = textRectangleTemplate.Text.GroupBy(x => x.Y)
                        .ToDictionary(x => x.Key, x => x.OrderBy(t => t.X).ToList());
                    foreach (var k in tempTemplateDict.Keys)
                    {
                        var list = tempTemplateDict[k];
                        delimetedText.Append(list[0].Text);
                        for (var i = 1; i < list.Count; i++)
                            if (Math.Abs(list[i].X - (list[i - 1].X + list[i - 1].Width)) < 1)
                                delimetedText.Append(list[i].Text);
                            else
                                delimetedText.Append(" ").Append(list[i].Text);
                        delimetedText.Append(" ");
                    }

                    if (textRectangleTemplate.HorizontalSpan > 1)
                        for (var i = 1; i < textRectangleTemplate.HorizontalSpan; i++)
                            delimetedText.Append(_delimeter);

                    if (index < templ.Count - 1)
                        delimetedText.Append(_delimeter);
                }

                delimetedText.AppendLine();
            }
            return delimetedText.ToString();
        }


        /// <summary>
        ///     Places text rectangles into templates
        /// </summary>
        /// <param name="textRect"></param>
        /// <param name="template"></param>
        /// <param name="yCoordKey"></param>
        /// <param name="overflowDelta"></param>
        private void PlaceTextRectIntoTemplates(List<TextRectangle> textRect,
            Dictionary<float, List<TextRectangleTemplate>> template, float yCoordKey, float overflowDelta)
        {
            if (textRect.Count > 0)
                foreach (var textRectangle in textRect)
                    foreach (var templateYcoord in template.Keys)
                    {
                        var maxHeight = template[templateYcoord].Max(x => x.Height);

                        if ((template[templateYcoord].Count > 0) && (templateYcoord >= textRectangle.Y) &&
                            (templateYcoord - maxHeight <= textRectangle.Y - textRectangle.Height))
                        {
                            var found = false;
                            foreach (var textRectangleTemplate in template[templateYcoord])
                                if ((textRectangle.X >= textRectangleTemplate.X) &&
                                    (textRectangle.X < textRectangleTemplate.X + textRectangleTemplate.Width))
                                {
                                    textRectangleTemplate.Text.Add(textRectangle);
                                    textRectangle.Assigned = true;
                                    found = true;
                                    break;
                                }
                            if (!found)
                            {
                                //need to adjust other cells
                                var ordered = template[templateYcoord].OrderBy(x => x.X).ToList();
                                var i = 0;
                                while ((textRectangle.X > ordered[i].X) && (i < ordered.Count))
                                    i++;
                                var xCoord = i == 0 ? 0 : ordered[i].X + ordered[i].Width;


                                foreach (var key in template.Keys)
                                {
                                    var yCoord = template[key].Max(x => x.Y);
                                    var newTempl = new TextRectangleTemplate
                                    {
                                        X = xCoord,
                                        Y = yCoord,
                                        Height = textRectangle.Height + overflowDelta,
                                        Width = ordered[i].X - xCoord,
                                        Text = new List<TextRectangle>()
                                    };
                                    template[key].Add(newTempl);
                                    if (Math.Abs(key - templateYcoord) < TOLERANCE)
                                        newTempl.Text.Add(textRectangle);
                                }


                                textRectangle.Assigned = true;
                            }
                        }
                    }

            textRect = textRect.Where(x => !x.Assigned).ToList();
            if (textRect.Count > 0)
            {
                if (!template.ContainsKey(yCoordKey))
                    template[yCoordKey] = new List<TextRectangleTemplate>();


                var templ = new TextRectangleTemplate {Text = textRect};
                template[yCoordKey].Add(templ);
            }
        }

        /// <summary>
        ///     Dumps text from dictionary as is in case then there is no graphic delimeters
        /// </summary>
        /// <param name="textDictionary"></param>
        /// <returns></returns>
        private string GenerateText(Dictionary<float, List<TextRectangle>> textDictionary)
        {
            var yCoord = textDictionary.Keys.ToList();
            yCoord.Sort();
            var delimetedText = new StringBuilder();
            foreach (var textY in yCoord)
            {
                var v = textDictionary[textY];
                var vals =
                    v.OrderBy(x => x.X)
                        .Select(x => x.Text)
                        .Aggregate((current, next) => current + _delimeter + " " + next);
                delimetedText.AppendLine(vals);
            }
            return delimetedText.ToString();
        }

        /// <summary>
        ///     Takes text and shapes and tries to arrange them so text is fitted in shapes in tabular format
        /// </summary>
        /// <param name="rectDictionary"></param>
        /// <param name="textDictionary"></param>
        /// <returns></returns>
        private Dictionary<float, List<TextRectangleTemplate>> ArrangeDictionariesIntoTemplates(
            Dictionary<float, List<Rectangle>> rectDictionary,
            Dictionary<float, List<TextRectangle>> textDictionary)
        {
            var verticalDivider = new HashSet<float>();
            var horizontalDivider = new HashSet<float>();

            var rectDictKeys = rectDictionary.Keys.OrderBy(x => x).ToList();

            var allrectangles = new List<Rectangle>();
            foreach (var rk in rectDictKeys)
            {
                var rowRectangles = rectDictionary[rk];
                allrectangles.AddRange(rowRectangles);
                foreach (var rowRectangle in rowRectangles)
                {
                    verticalDivider.Add(rowRectangle.X);
                    horizontalDivider.Add(rowRectangle.Y);
                    horizontalDivider.Add(rowRectangle.Y - rowRectangle.Height);
                }
            }

            var occurence = allrectangles.GroupBy(x => x.X).ToDictionary(x => x.Key, x => x.ToList());

            foreach (var o in occurence)
            {
                var endOfDiv = o.Value[0].X + o.Value[0].Width;
                if (o.Value.Count == 1)
                {
                    verticalDivider.Remove(o.Key);
                    if (rectDictionary.ContainsKey(o.Value[0].Y) && (rectDictionary[o.Value[0].Y].Count > 1))
                        for (var i = 0; i < rectDictionary[o.Value[0].Y].Count; i++)
                            if ((Math.Abs(rectDictionary[o.Value[0].Y][i].X - o.Value[0].X) < TOLERANCE) &&
                                (Math.Abs(rectDictionary[o.Value[0].Y][i].Width - o.Value[0].Width) < TOLERANCE))
                            {
                                rectDictionary[o.Value[0].Y].RemoveAt(i);
                                break;
                            }
                }
                else
                {
                    verticalDivider.Add(endOfDiv);
                }
            }


            var normalizeVertDividers = NormalizeDividers(verticalDivider);

            var templates = new List<TextRectangleTemplate>();

            foreach (var rectDictKey in rectDictKeys)
            {
                if (!rectDictionary.ContainsKey(rectDictKey) || (rectDictionary[rectDictKey].Count == 0))
                    continue;
                var minWidth = rectDictionary[rectDictKey].Select(x => x.Height).Distinct().OrderBy(x => x).First();
                foreach (var rowRectangle in rectDictionary[rectDictKey])
                    templates.AddRange(CreateTemplates(rowRectangle, normalizeVertDividers, minWidth));
            }


            AssignTextRectangles(templates, textDictionary);

            var returnDictionary = CreateTemplatesDictionary(templates, normalizeVertDividers);


            return returnDictionary;
        }

        /// <summary>
        ///     Creates a dictionary of text arranged inside shapes
        /// </summary>
        /// <param name="templates"></param>
        /// <param name="verticalDividers"></param>
        /// <returns></returns>
        private Dictionary<float, List<TextRectangleTemplate>> CreateTemplatesDictionary(
            List<TextRectangleTemplate> templates, List<float> verticalDividers)
        {
            var returnDictionary = new Dictionary<float, List<TextRectangleTemplate>>();
            var templateDictionary = templates.GroupBy(x => x.Y)
                .ToDictionary(x => x.Key, x => x.OrderBy(x1 => x1.X).ToList());

            foreach (var k in templateDictionary.Keys)
            {
                var textRectangleTemplates = templateDictionary[k];
                var averageHeight = textRectangleTemplates.Average(x => x.Height);

                if (textRectangleTemplates.Count != verticalDividers.Count - 1)
                {
                    var normalizedRow = new List<TextRectangleTemplate>();
                    for (var j = 0; j < verticalDividers.Count - 1; j++)
                        normalizedRow.Add(new TextRectangleTemplate
                        {
                            Text = new List<TextRectangle>(),
                            X = verticalDividers[j],
                            Width = verticalDividers[j + 1] - verticalDividers[j],
                            HorizontalSpan = 1,
                            VerticalSpan = 1,
                            Y = 0,
                            Height = averageHeight
                        });

                    for (var i = 0; i < normalizedRow.Count; i++)
                        foreach (var template in textRectangleTemplates)
                            if (Math.Abs(template.X - normalizedRow[i].X) < 2)
                                for (var p = 0; (p < template.HorizontalSpan) && (i + p < normalizedRow.Count); p++)
                                    normalizedRow[i + p].X = -1;

                    textRectangleTemplates.AddRange(normalizedRow.Where(x => x.X > 0));
                }


                returnDictionary[k] = textRectangleTemplates.OrderBy(x => x.X).ToList();
            }
            return returnDictionary;
        }


        /// <summary>
        ///     Assignes text to templates
        /// </summary>
        /// <param name="templates"></param>
        /// <param name="textDictionary"></param>
        private void AssignTextRectangles(List<TextRectangleTemplate> templates,
            Dictionary<float, List<TextRectangle>> textDictionary)
        {
            var tKeys = textDictionary.Keys.OrderBy(x => x).ToList();

            foreach (var textKey in tKeys)
                foreach (var textRectangle in textDictionary[textKey])
                    foreach (var textRectangleTemplate in templates)
                    {
                        if ((textRectangleTemplate.Y < textRectangle.Y - textRectangle.Height) ||
                            (textRectangleTemplate.Y - 20 > textRectangle.Y))
                            continue;

                        if (!textRectangle.Assigned && textRectangleTemplate.Contains(textRectangle, _charWidth))
                        {
                            textRectangleTemplate.Text.Add(textRectangle);
                            textRectangle.Assigned = true;
                            break;
                        }
                    }
        }

        /// <summary>
        ///     Creates templates representing text inside cell form rectangles
        /// </summary>
        /// <param name="rowRectangle"></param>
        /// <param name="verticalDividers"></param>
        /// <param name="minWidth"></param>
        private List<TextRectangleTemplate> CreateTemplates(Rectangle rowRectangle, List<float> verticalDividers,
            float minWidth)
        {
            var templates = new List<TextRectangleTemplate>();

            var added = false;
            for (var index = 1; index < verticalDividers.Count; index++)
            {
                var divider = verticalDividers[index];


                if (Math.Abs(divider - (rowRectangle.X + rowRectangle.Width)) < 2)
                {
                    //hom many cells to span
                    var j = index - 1;
                    var span = 0;
                    while (j >= 0)
                    {
                        span++;
                        if (Math.Abs(verticalDividers[j] - rowRectangle.X) < 2)
                            break;
                        j--;
                    }

                    //add missing templates

                    var templ = new TextRectangleTemplate
                    {
                        Text = new List<TextRectangle>(),
                        X = rowRectangle.X,
                        Width = rowRectangle.Width,
                        HorizontalSpan = span,
                        VerticalSpan = (int) (rowRectangle.Height/minWidth),
                        Y = rowRectangle.Y,
                        Height = rowRectangle.Height
                    };
                    templates.Add(templ);
                    added = true;
                }
                if (divider > rowRectangle.X + rowRectangle.Width + 1)
                {
                    if (!added)
                    {
                        var templ = new TextRectangleTemplate
                        {
                            Text = new List<TextRectangle>(),
                            X = rowRectangle.X,
                            Width = rowRectangle.Width,
                            HorizontalSpan = 1,
                            VerticalSpan = (int) (rowRectangle.Height/minWidth),
                            Y = rowRectangle.Y,
                            Height = rowRectangle.Height
                        };
                        templates.Add(templ);
                    }
                    break;
                }
            }

            return templates;
        }

        private List<float> NormalizeDividers(HashSet<float> rawDividers)
        {
            var dividers = rawDividers.OrderBy(x => x).ToList();

            var buckets = new List<float>();

            for (var l = 0; l < dividers.Count - 1; l++)
            {
                var found = false;
                for (var k = 0; k < buckets.Count; k++)
                    if (Math.Abs(buckets[k] - dividers[l]) < 2)
                    {
                        buckets[k] = (buckets[k] + dividers[l])/2;
                        found = true;
                        break;
                    }
                if (!found)
                    buckets.Add(dividers[l]);
            }
            if (dividers.Count > 0)
                buckets.Add(dividers[dividers.Count - 1]);
            return buckets;
        }

        #region IExtRenderListener

        /// <summary>
        ///     Modifies the current path in pdf construction
        /// </summary>
        /// <param name="renderInfo">
        ///     Contains information relating to construction the current path.
        /// </param>
        /// We are interested only in straight lines and rectangles
        public void ModifyPath(PathConstructionRenderInfo renderInfo)
        {
            var segmentData = renderInfo.SegmentData;

            switch (renderInfo.Operation)
            {
                case PathConstructionRenderInfo.MOVETO:
                    _currentPoints.Add(new LineMove {X = segmentData[0], Y = segmentData[1], IsMove = true});
                    break;
                case PathConstructionRenderInfo.LINETO:
                    _currentPoints.Add(new LineMove {X = segmentData[0], Y = segmentData[1], IsMove = false});

                    break;
                case PathConstructionRenderInfo.CURVE_123:
                case PathConstructionRenderInfo.CURVE_13:
                case PathConstructionRenderInfo.CURVE_23:
                    break;
                case PathConstructionRenderInfo.RECT:
                    var x = segmentData[0];
                    var y = segmentData[1];
                    var width = segmentData[2];
                    var height = segmentData[3];
                    _currentRectangle = new Rectangle {X = x, Height = height, Width = width, Y = y};
                    break;
                case PathConstructionRenderInfo.CLOSE:

                    break;
            }
        }

        /// <summary>
        ///     Not used
        /// </summary>
        /// <param name="rule"></param>
        public void ClipPath(int rule)
        {
        }

        /// <summary>
        ///     Appends new rectangle to rectangles List
        ///     or tries to construct a rectangle from individual lines
        /// </summary>
        /// <param name="renderInfo"></param>
        /// <returns></returns>
        public Path RenderPath(PathPaintingRenderInfo renderInfo)
        {
            if (renderInfo.Operation != PathPaintingRenderInfo.NO_OP)
            {
                if (_currentRectangle != null)
                {
                    var transformedRect = Transform(_currentRectangle, renderInfo.Ctm);
                    var maxDimension = Math.Max(_pageWidth, _pageHeight);

                    if (transformedRect.X + _currentRectangle.Width > maxDimension)
                        _currentRectangle.Width = maxDimension - transformedRect.X;


                    _rectangles.Add(new Rectangle
                    {
                        Height = _currentRectangle.Height,
                        Width = _currentRectangle.Width,
                        X = transformedRect.X,
                        Y = transformedRect.Y
                    });

                    return null;
                }
                ConstructShape(renderInfo.Ctm);
            }
            _currentPoints.Clear();

            return null;
        }

        #endregion

        #region LocationTextExtractionStrategyoverrides

        /// <summary>
        ///     places text in text rectangles as pdf might render each character separetly
        /// </summary>
        /// <param name="renderInfo"></param>
        public override void RenderText(TextRenderInfo renderInfo)
        {
            var segment = renderInfo.GetBaseline();

            _charWidth = renderInfo.GetSingleSpaceWidth()/2f;
            var text = renderInfo.GetText();
            _log.Debug("RenderText " + text);

            var bottomLeftCoordinate = renderInfo.GetDescentLine().GetStartPoint();
            var topRightCoordinate = renderInfo.GetAscentLine().GetEndPoint();

            var textRectangle = new TextRectangle
            {
                Text = text,
                X = bottomLeftCoordinate[0],
                Y = bottomLeftCoordinate[1],
                Width = topRightCoordinate[0] - bottomLeftCoordinate[0],
                Height = Math.Abs(bottomLeftCoordinate[1] - topRightCoordinate[1])
            };
            if (_pageRotation == 0)
                textRectangle.Y = _pageHeight - textRectangle.Y;
            if (_pageRotation == 90)
            {
                textRectangle.X = bottomLeftCoordinate[1];
                textRectangle.Y = bottomLeftCoordinate[0];
                textRectangle.Width = Math.Abs(topRightCoordinate[1] - bottomLeftCoordinate[1]);
                textRectangle.Height = Math.Abs(bottomLeftCoordinate[0] - topRightCoordinate[0]);
            }

            var startLocation = segment.GetStartPoint();
            var endLocation = segment.GetEndPoint();


            var oVector = endLocation.Subtract(startLocation);
            if (oVector.Length == 0)
                oVector = new Vector(1, 0, 0);

            var orientationVector = oVector.Normalize();
            var orientationMagnitude =
                (int) (Math.Atan2(orientationVector[Vector.I2], orientationVector[Vector.I1])*1000);

            if (orientationMagnitude != 0)
                if (orientationVector[1] == -1)
                    textRectangle.Y = textRectangle.Y + textRectangle.Height;


            if ((_lastChunk != null) && (Math.Abs(_lastChunk.Y - textRectangle.Y) < TOLERANCE))
            {
                var spacing = Math.Abs(textRectangle.X - (_lastChunk.X + _lastChunk.Width));
                if (spacing < renderInfo.GetSingleSpaceWidth()/2f)
                {
                    _lastChunk.Width += textRectangle.Width;
                    _lastChunk.Text += textRectangle.Text;
                    return;
                }
            }
            else if ((_lastChunk != null) && (orientationVector[1] == -1) &&
                     (Math.Abs(_lastChunk.X - textRectangle.X) < TOLERANCE))
            {
                var spacing = Math.Abs(textRectangle.Y - (_lastChunk.Y + textRectangle.Height));
                if (spacing < renderInfo.GetSingleSpaceWidth()/2f)
                {
                    _lastChunk.Height += textRectangle.Height;
                    _lastChunk.Text += textRectangle.Text;
                    return;
                }
            }


            _lastChunk = textRectangle;

            _textRectangles.Add(textRectangle);
        }


        /// <summary>
        ///     Overriden Itextsharp function rendering all text
        /// </summary>
        /// <returns></returns>
        public override string GetResultantText()
        {
            var resultantText = new StringBuilder();
            if (_textRectangles.Count == 0)
                return "";

            var maxLineHeight = Math.Ceiling(_textRectangles.Max(x => x.Height))*2;
            var textAndRectYCoord =
                _textRectangles.Union(_rectangles).GroupBy(x => x.Y).Select(x => x.Key).OrderBy(x => x).ToList();


            float begin = 0;
            var end = textAndRectYCoord.Last();
            List<TextRectangle> text;
            List<Rectangle> rect;
            for (var i = 1; i < textAndRectYCoord.Count; i++)
                if (textAndRectYCoord[i] - textAndRectYCoord[i - 1] > maxLineHeight)
                {
                    text = _textRectangles.Where(x => (x.Y < textAndRectYCoord[i]) && (x.Y >= begin)).ToList();
                    rect = _rectangles.Where(x => (x.Y < textAndRectYCoord[i]) && (x.Y >= begin)).ToList();
                    begin = textAndRectYCoord[i];
                    if (text.Count > 0)
                        resultantText.Append(GetResultantTextInternal(text, rect));
                }

            text = _textRectangles.Where(x => (x.Y <= end) && (x.Y >= begin)).ToList();
            rect = _rectangles.Where(x => (x.Y <= end) && (x.Y >= begin)).ToList();
            if (text.Count > 0)
                resultantText.Append(GetResultantTextInternal(text, rect));


            return resultantText.ToString();
        }

        #endregion
    }
}