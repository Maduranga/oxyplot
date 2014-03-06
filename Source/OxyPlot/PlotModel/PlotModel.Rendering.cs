﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PlotModel.Rendering.cs" company="OxyPlot">
//   The MIT License (MIT)
//   
//   Copyright (c) 2012 Oystein Bjorke
//   
//   Permission is hereby granted, free of charge, to any person obtaining a
//   copy of this software and associated documentation files (the
//   "Software"), to deal in the Software without restriction, including
//   without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to
//   permit persons to whom the Software is furnished to do so, subject to
//   the following conditions:
//   
//   The above copyright notice and this permission notice shall be included
//   in all copies or substantial portions of the Software.
//   
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
//   OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//   MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
//   IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
//   CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
//   TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
//   SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// <summary>
//   Partial PlotModel class - this file contains rendering methods.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace OxyPlot
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using OxyPlot.Annotations;
    using OxyPlot.Axes;
    using OxyPlot.Series;

    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1601:PartialElementsMustBeDocumented", Justification = "Reviewed. Suppression is OK here.")]
    public partial class PlotModel
    {
        /// <summary>
        /// Renders the plot with the specified rendering context.
        /// </summary>
        /// <param name="rc">The rendering context.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        public void Render(IRenderContext rc, double width, double height)
        {
            lock (this.syncRoot)
            {
                if (width <= 0 || height <= 0)
                {
                    return;
                }

                this.Width = width;
                this.Height = height;

                this.ActualPlotMargins = this.PlotMargins;
                this.EnsureLegendProperties();

                while (true)
                {
                    this.UpdatePlotArea(rc);
                    this.UpdateAxisTransforms();
                    this.UpdateIntervals();
                    if (!this.AutoAdjustPlotMargins)
                    {
                        break;
                    }

                    if (!this.AdjustPlotMargins(rc))
                    {
                        break;
                    }
                }

                if (this.PlotType == PlotType.Cartesian)
                {
                    this.EnforceCartesianTransforms();
                    this.UpdateIntervals();
                }

                foreach (var a in this.Axes)
                {
                    a.ResetCurrentValues();
                }

                this.RenderBackgrounds(rc);
                this.RenderAnnotations(rc, AnnotationLayer.BelowAxes);
                this.RenderAxes(rc, AxisLayer.BelowSeries);
                this.RenderAnnotations(rc, AnnotationLayer.BelowSeries);
                this.RenderSeries(rc);
                this.RenderAnnotations(rc, AnnotationLayer.AboveSeries);
                this.RenderTitle(rc);
                this.RenderBox(rc);
                this.RenderAxes(rc, AxisLayer.AboveSeries);

                if (this.IsLegendVisible)
                {
                    this.RenderLegends(rc, this.LegendArea);
                }

                // Clean up unused images
                rc.CleanUp();
            }
        }

        /// <summary>
        /// Increase margin size if needed, do it on all borders
        /// </summary>
        /// <param name="currentMargin">The current margin.</param>
        /// <param name="minBorderSize">Minimum size of the border.</param>
        private static void EnsureMarginIsBigEnough(ref OxyThickness currentMargin, double minBorderSize)
        {
            currentMargin.Bottom = Math.Max(currentMargin.Bottom, minBorderSize);
            currentMargin.Left = Math.Max(currentMargin.Left, minBorderSize);
            currentMargin.Right = Math.Max(currentMargin.Right, minBorderSize);
            currentMargin.Top = Math.Max(currentMargin.Top, minBorderSize);
        }

        /// <summary>
        /// Increase margin size if needed, do it on the specified border.
        /// </summary>
        /// <param name="currentMargin">The current margin.</param>
        /// <param name="minBorderSize">Minimum size of the border.</param>
        /// <param name="borderPosition">The border position.</param>
        private static void EnsureMarginIsBigEnough(ref OxyThickness currentMargin, double minBorderSize, AxisPosition borderPosition)
        {
            switch (borderPosition)
            {
                case AxisPosition.Bottom:
                    currentMargin.Bottom = Math.Max(currentMargin.Bottom, minBorderSize);
                    break;

                case AxisPosition.Left:
                    currentMargin.Left = Math.Max(currentMargin.Left, minBorderSize);
                    break;

                case AxisPosition.Right:
                    currentMargin.Right = Math.Max(currentMargin.Right, minBorderSize);
                    break;

                case AxisPosition.Top:
                    currentMargin.Top = Math.Max(currentMargin.Top, minBorderSize);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Calculates the maximum size of the specified axes.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        /// <param name="axesOfPositionTier">
        /// The axes of position tier.
        /// </param>
        /// <returns>
        /// The maximum size.
        /// </returns>
        private static double MaxSizeOfPositionTier(IRenderContext rc, IEnumerable<Axis> axesOfPositionTier)
        {
            double maxSizeOfPositionTier = 0;
            foreach (var axis in axesOfPositionTier)
            {
                var size = axis.Measure(rc);
                if (axis.IsVertical())
                {
                    if (size.Width > maxSizeOfPositionTier)
                    {
                        maxSizeOfPositionTier = size.Width;
                    }
                }
                else
                {
                    // caution: this includes AngleAxis because Position=None
                    if (size.Height > maxSizeOfPositionTier)
                    {
                        maxSizeOfPositionTier = size.Height;
                    }
                }
            }

            return maxSizeOfPositionTier;
        }

        /// <summary>
        /// Adjust the plot margins.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        /// <returns>
        /// The adjust plot margins.
        /// </returns>
        private bool AdjustPlotMargins(IRenderContext rc)
        {
            var currentMargin = this.ActualPlotMargins;

            for (var position = AxisPosition.Left; position <= AxisPosition.Bottom; position++)
            {
                var axesOfPosition = this.VisibleAxes.Where(a => a.Position == position).ToList();

                var requiredSize = this.AdjustAxesPositions(rc, axesOfPosition);

                EnsureMarginIsBigEnough(ref currentMargin, requiredSize, position);
            }

            // Special case for AngleAxis which is all around the plot
            var angularAxes = this.VisibleAxes.OfType<AngleAxis>().Cast<Axis>().ToList();

            if (angularAxes.Any())
            {
                var requiredSize = this.AdjustAxesPositions(rc, angularAxes);

                EnsureMarginIsBigEnough(ref currentMargin, requiredSize);
            }

            if (currentMargin.Equals(this.ActualPlotMargins))
            {
                return false;
            }

            this.ActualPlotMargins = currentMargin;
            return true;
        }

        /// <summary>
        /// Adjust the positions of parallel axes, returns total size
        /// </summary>
        /// <param name="rc">The render context.</param>
        /// <param name="parallelAxes">The parallel axes.</param>
        /// <returns>The maximum value of the position tier??</returns>
        private double AdjustAxesPositions(IRenderContext rc, IList<Axis> parallelAxes)
        {
            double maxValueOfPositionTier = 0;

            foreach (var positionTier in parallelAxes.Select(a => a.PositionTier).Distinct().OrderBy(l => l))
            {
                var axesOfPositionTier = parallelAxes.Where(a => a.PositionTier == positionTier).ToList();
                var maxSizeOfPositionTier = MaxSizeOfPositionTier(rc, axesOfPositionTier);
                var minValueOfPositionTier = maxValueOfPositionTier;

                if (Math.Abs(maxValueOfPositionTier) > 1e-5)
                {
                    maxValueOfPositionTier += this.AxisTierDistance;
                }

                maxValueOfPositionTier += maxSizeOfPositionTier;

                foreach (var axis in axesOfPositionTier)
                {
                    axis.PositionTierSize = maxSizeOfPositionTier;
                    axis.PositionTierMinShift = minValueOfPositionTier;
                    axis.PositionTierMaxShift = maxValueOfPositionTier;
                }
            }

            return maxValueOfPositionTier;
        }

        /// <summary>
        /// Measures the size of the title and subtitle.
        /// </summary>
        /// <param name="rc">
        /// The rendering context.
        /// </param>
        /// <returns>
        /// Size of the titles.
        /// </returns>
        private OxySize MeasureTitles(IRenderContext rc)
        {
            OxySize size1 = rc.MeasureText(this.Title, this.ActualTitleFont, this.TitleFontSize, this.TitleFontWeight);
            OxySize size2 = rc.MeasureText(
                this.Subtitle, this.SubtitleFont ?? this.ActualSubtitleFont, this.SubtitleFontSize, this.SubtitleFontWeight);
            double height = size1.Height + size2.Height;
            double width = Math.Max(size1.Width, size2.Width);
            return new OxySize(width, height);
        }

        /// <summary>
        /// Renders the annotations.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        /// <param name="layer">
        /// The layer.
        /// </param>
        private void RenderAnnotations(IRenderContext rc, AnnotationLayer layer)
        {
            foreach (var a in this.Annotations.Where(a => a.Layer == layer))
            {
                a.Render(rc, this);
            }
        }

        /// <summary>
        /// Renders the axes.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        /// <param name="layer">
        /// The layer.
        /// </param>
        private void RenderAxes(IRenderContext rc, AxisLayer layer)
        {
            for (int i = 0; i < 2; i++)
            {
                foreach (var a in this.VisibleAxes)
                {
                    if (a.Layer == layer)
                    {
                        a.Render(rc, this, layer, i);
                    }
                }
            }
        }

        /// <summary>
        /// Renders the series backgrounds.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        private void RenderBackgrounds(IRenderContext rc)
        {
            // Render the main background of the plot area (only if there are axes)
            // The border is rendered by DrawRectangleAsPolygon to ensure that it is pixel aligned with the tick marks.
            if (this.Axes.Count > 0 && this.PlotAreaBackground.IsVisible())
            {
                rc.DrawRectangleAsPolygon(this.PlotArea, this.PlotAreaBackground, OxyColors.Undefined, 0);
            }

            foreach (var s in this.VisibleSeries)
            {
                var s2 = s as XYAxisSeries;
                if (s2 == null || s2.Background.IsInvisible())
                {
                    continue;
                }

                rc.DrawRectangle(s2.GetScreenRectangle(), s2.Background, OxyColors.Undefined, 0);
            }
        }

        /// <summary>
        /// Renders the border around the plot area.
        /// </summary>
        /// <remarks>
        /// The border will only by rendered if there are axes in the plot.
        /// </remarks>
        /// <param name="rc">
        /// The render context.
        /// </param>
        private void RenderBox(IRenderContext rc)
        {
            // The border is rendered by DrawBox to ensure that it is pixel aligned with the tick marks (cannot use DrawRectangle here).
            if (this.Axes.Count > 0)
            {
                rc.DrawRectangleAsPolygon(this.PlotArea, OxyColors.Undefined, this.PlotAreaBorderColor, this.PlotAreaBorderThickness);
            }
        }

        /// <summary>
        /// Renders the series.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        private void RenderSeries(IRenderContext rc)
        {
            // Update undefined colors
            this.ResetDefaultColor();
            foreach (var s in this.VisibleSeries)
            {
                s.SetDefaultValues(this);
            }

            foreach (var s in this.VisibleSeries)
            {
                s.Render(rc, this);
            }
        }

        /// <summary>
        /// Renders the title and subtitle.
        /// </summary>
        /// <param name="rc">
        /// The render context.
        /// </param>
        private void RenderTitle(IRenderContext rc)
        {
            OxySize size1 = rc.MeasureText(this.Title, this.ActualTitleFont, this.TitleFontSize, this.TitleFontWeight);
            rc.MeasureText(
                this.Subtitle, this.SubtitleFont ?? this.ActualSubtitleFont, this.SubtitleFontSize, this.SubtitleFontWeight);

            // double height = size1.Height + size2.Height;
            // double dy = (TitleArea.Top+TitleArea.Bottom-height)*0.5;
            double dy = this.TitleArea.Top;
            double dx = (this.TitleArea.Left + this.TitleArea.Right) * 0.5;

            if (!string.IsNullOrEmpty(this.Title))
            {
                rc.DrawMathText(
                    new ScreenPoint(dx, dy),
                    this.Title,
                    this.TitleColor.GetActualColor(this.TextColor),
                    this.ActualTitleFont,
                    this.TitleFontSize,
                    this.TitleFontWeight,
                    0,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Top);
                dy += size1.Height;
            }

            if (!string.IsNullOrEmpty(this.Subtitle))
            {
                rc.DrawMathText(
                    new ScreenPoint(dx, dy),
                    this.Subtitle,
                    this.SubtitleColor.GetActualColor(this.TextColor),
                    this.ActualSubtitleFont,
                    this.SubtitleFontSize,
                    this.SubtitleFontWeight,
                    0,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Top);
            }
        }

        /// <summary>
        /// Calculates the plot area (subtract padding, title size and outside legends)
        /// </summary>
        /// <param name="rc">
        /// The rendering context.
        /// </param>
        private void UpdatePlotArea(IRenderContext rc)
        {
            var plotArea = new OxyRect(
                this.Padding.Left,
                this.Padding.Top,
                this.Width - this.Padding.Left - this.Padding.Right,
                this.Height - this.Padding.Top - this.Padding.Bottom);

            var titleSize = this.MeasureTitles(rc);

            if (titleSize.Height > 0)
            {
                double titleHeight = titleSize.Height + this.TitlePadding;
                plotArea.Height -= titleHeight;
                plotArea.Top += titleHeight;
            }

            plotArea.Top += this.ActualPlotMargins.Top;
            plotArea.Height -= this.ActualPlotMargins.Top;

            plotArea.Height -= this.ActualPlotMargins.Bottom;

            plotArea.Left += this.ActualPlotMargins.Left;
            plotArea.Width -= this.ActualPlotMargins.Left;

            plotArea.Width -= this.ActualPlotMargins.Right;

            // Find the available size for the legend box
            double availableLegendWidth = plotArea.Width;
            double availableLegendHeight = plotArea.Height;
            if (this.LegendPlacement == LegendPlacement.Inside)
            {
                availableLegendWidth -= this.LegendMargin * 2;
                availableLegendHeight -= this.LegendMargin * 2;
            }

            if (availableLegendWidth < 0)
            {
                availableLegendWidth = 0;
            }

            if (availableLegendHeight < 0)
            {
                availableLegendHeight = 0;
            }

            // Calculate the size of the legend box
            var legendSize = this.MeasureLegends(rc, new OxySize(availableLegendWidth, availableLegendHeight));

            // Adjust the plot area after the size of the legend box has been calculated
            if (this.IsLegendVisible && this.LegendPlacement == LegendPlacement.Outside)
            {
                switch (this.LegendPosition)
                {
                    case LegendPosition.LeftTop:
                    case LegendPosition.LeftMiddle:
                    case LegendPosition.LeftBottom:
                        plotArea.Left += legendSize.Width + this.LegendMargin;
                        plotArea.Width -= legendSize.Width + this.LegendMargin;
                        break;
                    case LegendPosition.RightTop:
                    case LegendPosition.RightMiddle:
                    case LegendPosition.RightBottom:
                        plotArea.Width -= legendSize.Width + this.LegendMargin;
                        break;
                    case LegendPosition.TopLeft:
                    case LegendPosition.TopCenter:
                    case LegendPosition.TopRight:
                        plotArea.Top += legendSize.Height + this.LegendMargin;
                        plotArea.Height -= legendSize.Height + this.LegendMargin;
                        break;
                    case LegendPosition.BottomLeft:
                    case LegendPosition.BottomCenter:
                    case LegendPosition.BottomRight:
                        plotArea.Height -= legendSize.Height + this.LegendMargin;
                        break;
                }
            }

            // Ensure the plot area is valid
            if (plotArea.Height < 0)
            {
                plotArea.Bottom = plotArea.Top + 1;
            }

            if (plotArea.Width < 0)
            {
                plotArea.Right = plotArea.Left + 1;
            }

            this.PlotArea = plotArea;
            this.PlotAndAxisArea = new OxyRect(
                plotArea.Left - this.ActualPlotMargins.Left,
                plotArea.Top - this.ActualPlotMargins.Top,
                plotArea.Width + this.ActualPlotMargins.Left + this.ActualPlotMargins.Right,
                plotArea.Height + this.ActualPlotMargins.Top + this.ActualPlotMargins.Bottom);
            this.TitleArea = new OxyRect(this.PlotArea.Left, this.Padding.Top, this.PlotArea.Width, titleSize.Height + (this.TitlePadding * 2));
            this.LegendArea = this.GetLegendRectangle(legendSize);
        }
    }
}