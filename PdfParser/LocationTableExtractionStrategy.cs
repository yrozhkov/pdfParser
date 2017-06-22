using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTextSharp.awt.geom;
using iTextSharp.text.pdf.parser;
using MoreLinq;
using PdfParser.pdfObjects;

using Point = PdfParser.pdfObjects.Point;
using Logging;
using Line = PdfParser.pdfObjects.Line;

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

        private readonly float _pageHeight;
        private readonly int _pageRotation;
        private readonly float _pageWidth;
        private readonly List<Rectangle> _rectangles = new List<Rectangle>();
        private readonly List<Line> _lines = new List<Line>();
     
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


        /// <summary>
        ///     places text in text rectangles as pdf might render each character separetly
        /// </summary>
        /// <param name="renderInfo"></param>
        public override void RenderText(TextRenderInfo renderInfo)
        {
            var segment = renderInfo.GetBaseline();

            _charWidth = renderInfo.GetSingleSpaceWidth()/2f;
            var text = renderInfo.GetText();
            _log.Debug("RenderText "+ text);

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
                default:
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


            List<Point> points = null;

            if (_currentPoints.Count > 0)
            {
                points = new List<Point>();
                foreach (var currentPoint in _currentPoints)
                    points.Add(Transform(currentPoint, ctm));
                points = points.DistinctBy(p => new {p.X, p.Y}).ToList();
            }

            else
                return;

            if (points.Count == 4)
            {
                //rect
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
                Point beginPoint = Transform(_currentPoints[0], ctm);
                Point endPoint = Transform(_currentPoints[1], ctm);
                _lines.Add(new Line (beginPoint,endPoint));
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
                return new Point(dst.x,_pageHeight - dst.y);

            if (_pageRotation == 90)
                return new Point (dst.x, dst.y);

            throw new Exception( " Current page rotation is not handled");
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


            var textandlinesYcoordinates = _textRectangles.GroupBy(x => x.Y).Select(x => x.Key).ToList();
            var rectKeys = _rectangles.GroupBy(x => x.Y).Select(x => x.Key).ToList();
            textandlinesYcoordinates.AddRange(rectKeys);

            textandlinesYcoordinates.Sort();

            float begin = 0;
            var end = textandlinesYcoordinates.Last();
            List<TextRectangle> text;
            List<Rectangle> rect;
            for (var i = 1; i < textandlinesYcoordinates.Count; i++)
                if (textandlinesYcoordinates[i] - textandlinesYcoordinates[i - 1] > maxLineHeight)
                {
                    text = _textRectangles.Where(x => (x.Y < textandlinesYcoordinates[i]) && (x.Y >= begin)).ToList();
                    rect = _rectangles.Where(x => (x.Y < textandlinesYcoordinates[i]) && (x.Y >= begin)).ToList();
                    begin = textandlinesYcoordinates[i];
                    if (text.Count > 0)
                        resultantText.Append(GetResultantTextInternal(text, rect));
                }

            text = _textRectangles.Where(x => (x.Y <= end) && (x.Y >= begin)).ToList();
            rect = _rectangles.Where(x => (x.Y <= end) && (x.Y >= begin)).ToList();
            if (text.Count > 0)
                resultantText.Append(GetResultantTextInternal(text, rect));


            return resultantText.ToString();
        }


        private string GetResultantTextInternal(List<TextRectangle> textChunks, List<Rectangle> rectangles)
        {
            var mostCommoncellHeight = rectangles.Count > 0
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


            var textDictionary = StrategyHelper.GetTextDictionary(textChunks, overflowDelta);
            var rectDictionary = rectangles.Count > 0
                ? StrategyHelper.GetRectDictionary(rectangles, mostCommoncellHeight, minHeight, minWidth)
                : new Dictionary<float, List<Rectangle>>();


            var delimetedText = new StringBuilder();
            //We did not find any shapes so just   print the text as is
            if (rectDictionary.Count == 0)
                return GenerateText(textDictionary);

            //Not used yet
            //  var vlines = StrategyHelper.GetVerticalLines(_lines);
            //  var hlines = StrategyHelper.GetHorizontalLines(_lines);

            var template = ArrangeDictionaries(rectDictionary, textDictionary);


            foreach (var yCoordKey in textDictionary.Keys)
            {
                var textRect = textDictionary[yCoordKey].Where(x => !x.Assigned).ToList();
                PlacetextRectIntoTemplates(textRect, template, yCoordKey, overflowDelta);
            }

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
                    //if (textRectangleTemplate.VerticalSpan > 1)
                    //{

                    //}
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
        /// <param name="tolerance"></param>
        private void PlacetextRectIntoTemplates(List<TextRectangle> textRect,
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
                                while (textRectangle.X > ordered[i].X)
                                {
                                    if (i == ordered.Count - 1) break;


                                    i++;
                                }


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


                var templ = new TextRectangleTemplate();
                templ.Text = textRect;
                template[yCoordKey].Add(templ);
            }
        }

        /// <summary>
        ///     Dumps text from dictionary as is
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
        private Dictionary<float, List<TextRectangleTemplate>> ArrangeDictionaries(
            Dictionary<float, List<Rectangle>> rectDictionary,
            Dictionary<float, List<TextRectangle>> textDictionary)
        {
             

            var verticalDivider = new HashSet<float>();
            var horizontalDivider = new HashSet<float>();

            var rKeys = rectDictionary.Keys.OrderBy(x => x).ToList();

            var allrectangles = new List<Rectangle>();
            foreach (var rk in rKeys)
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
            //  var horizontalDividers = NormalizeDividers(hDividers);
            var templates = new List<TextRectangleTemplate>();

            for (var i = 0; i < rKeys.Count; i++)
            {
                if (!rectDictionary.ContainsKey(rKeys[i]) || (rectDictionary[rKeys[i]].Count == 0))
                    continue;
                var minWidth = rectDictionary[rKeys[i]].Select(x => x.Height).Distinct().OrderBy(x => x).First();
                foreach (var rowRectangle in rectDictionary[rKeys[i]])
                    templates.AddRange(CreateTemplates(rowRectangle, normalizeVertDividers, minWidth));
            }


            AssignTextRectangles(templates, textDictionary);

            var returnDictionary = CreateReturnDictionary(templates, normalizeVertDividers);


            return returnDictionary;
        }

        /// <summary>
        ///     Creates a dictionary of text arranged inside shapes
        /// </summary>
        /// <param name="templates"></param>
        /// <param name="verticalDividers"></param>
        /// <returns></returns>
        private Dictionary<float, List<TextRectangleTemplate>> CreateReturnDictionary(
            List<TextRectangleTemplate> templates, List<float> verticalDividers)
        {
            var returnDictionary = new Dictionary<float, List<TextRectangleTemplate>>();
            var tDict = templates.GroupBy(x => x.Y).ToDictionary(x => x.Key, x => x.OrderBy(x1 => x1.X).ToList());
            var keys = tDict.Keys;


            foreach (var k in keys)
            {
                var templ = tDict[k];
                var h = templ.Average(x => x.Height);

                if (templ.Count != verticalDividers.Count - 1)
                {
                    var perfectRow = new List<TextRectangleTemplate>();
                    for (var j = 0; j < verticalDividers.Count - 1; j++)
                        perfectRow.Add(new TextRectangleTemplate
                        {
                            Text = new List<TextRectangle>(),
                            X = verticalDividers[j],
                            Width = verticalDividers[j + 1] - verticalDividers[j],
                            HorizontalSpan = 1,
                            VerticalSpan = 1,
                            Y = 0,
                            Height = h
                        });

                    for (var i = 0; i < perfectRow.Count; i++)
                        for (var j = 0; j < templ.Count; j++)
                            if (Math.Abs(templ[j].X - perfectRow[i].X) < 2)
                                for (var p = 0; (p < templ[j].HorizontalSpan) && (i + p < perfectRow.Count); p++)
                                    perfectRow[i + p].X = -1;

                    templ.AddRange(perfectRow.Where(x => x.X > 0));
                }


                returnDictionary[k] = templ.OrderBy(x => x.X).ToList();
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

        private List<float> NormalizeDividers(HashSet<float> vDividers)
        {
            var dividers = vDividers.OrderBy(x => x).ToList();

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
    }
}