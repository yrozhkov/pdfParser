﻿using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using PdfParser.pdfObjects;

namespace PdfParser
{
    public static class StrategyHelper
    {
        private static readonly double TOLERANCE = 0.00001;

        /// <summary>
        ///     groups all text by lines
        /// </summary>
        /// <param name="textRectangles"></param>
        /// <param name="overflowDelta"></param>
        /// <returns></returns>
        public static Dictionary<float, List<TextRectangle>> GetTextDictionary(List<TextRectangle> textRectangles,
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
        private static void HandleDisplaced(HashSet<float> displacedCoordinates,
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
        public static Dictionary<float, List<Rectangle>> GetRectDictionary(List<Rectangle> rectangles, float avgHeight,
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
    }
}