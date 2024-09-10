﻿using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Maui.Platform;
using Windows.Storage.Streams;
using Windows.UI.Input.Inking;
using Point = Windows.Foundation.Point;
using Rect = Windows.Foundation.Rect;
using Size = Microsoft.Maui.Graphics.Size;

namespace CommunityToolkit.Maui.Core.Views;

/// <summary>
///     Drawing view service
/// </summary>
public static partial class DrawingViewService
{
	/// <summary>
	/// Get image stream from lines
	/// </summary>
	/// <param name="lines">Drawing lines</param>
	/// <param name="desiredSize">Maximum image size. The image will be resized proportionally.</param>
	/// <param name="background">Image background</param>
	/// <param name="canvasSize">
	/// The actual size of the canvas being displayed. This is an optional parameter
	/// if a value is provided then the contents of the <paramref name="lines"/> inside these dimensions will be included in the output,
	/// if <c>null</c> is provided then the resulting output will be the area covered by the top-left to the bottom-right most points.
	/// </param>
	/// <param name="token"><see cref="CancellationToken"/></param>
	/// <returns>Image stream</returns>
	public static ValueTask<Stream> GetImageStream(IList<IDrawingLine> lines, Size desiredSize, Paint? background, Size? canvasSize, CancellationToken token = default)
	{
		var canvas = GetImageInternal(lines, desiredSize, background, canvasSize);

		return GetCanvasRenderTargetStream(canvas, token);
	}

	/// <summary>
	/// Get image stream from points
	/// </summary>
	/// <param name="points">Drawing points</param>
	/// <param name="desiredSize">Maximum image size. The image will be resized proportionally.</param>
	/// <param name="lineWidth">Line Width</param>
	/// <param name="strokeColor">Line color</param>
	/// <param name="background">Image background</param>
	/// <param name="canvasSize">
	/// The actual size of the canvas being displayed. This is an optional parameter
	/// if a value is provided then the contents of the <paramref name="points"/> inside these dimensions will be included in the output,
	/// if <c>null</c> is provided then the resulting output will be the area covered by the top-left to the bottom-right most points.
	/// </param>
	/// <param name="token"><see cref="CancellationToken"/></param>
	/// <returns>Image stream</returns>
	public static ValueTask<Stream> GetImageStream(IList<PointF> points, Size desiredSize, float lineWidth, Color strokeColor, Paint? background, Size? canvasSize, CancellationToken token = default)
	{
		var canvas = GetImageInternal(points, desiredSize, lineWidth, strokeColor, background, canvasSize);

		return GetCanvasRenderTargetStream(canvas, token);
	}

	static async ValueTask<Stream> GetCanvasRenderTargetStream(CanvasRenderTarget? canvas, CancellationToken token)
	{
		if (canvas is null)
		{
			return Stream.Null;
		}

		using (canvas)
		{
			var fileStream = new InMemoryRandomAccessStream();
			await canvas.SaveAsync(fileStream, CanvasBitmapFileFormat.Png).AsTask(token);

			var stream = fileStream.AsStream();
			stream.Position = 0;

			return stream;
		}
	}

	static CanvasRenderTarget? GetImageInternal(ICollection<PointF> points,
		Size size,
		float lineWidth,
		Color lineColor,
		Paint? background,
		Size? canvasSize,
		bool scale = false)
	{
		var (offscreen, offset) = GetCanvasRenderTarget(points, size, scale, lineWidth, canvasSize);
		if (offscreen is null)
		{
			return null;
		}

		using var session = offscreen.CreateDrawingSession();
		var brush = GetCanvasBrush(session, background, offscreen.Size);
		session.FillRectangle(new Rect(0, 0, offscreen.Size.Width, offscreen.Size.Height), brush);

		DrawStrokes(session, points, lineColor, lineWidth, offset);

		return offscreen;
	}

	static void DrawStrokes(CanvasDrawingSession session,
		IEnumerable<PointF> points,
		Color lineColor,
		float lineWidth,
		Size offset)
	{
		var strokeBuilder = new InkStrokeBuilder();
		var inkDrawingAttributes = new InkDrawingAttributes
		{
			Color = lineColor.ToWindowsColor(),
			Size = new Windows.Foundation.Size(lineWidth, lineWidth)
		};
		strokeBuilder.SetDefaultDrawingAttributes(inkDrawingAttributes);
		var strokes = new[]
		{
			strokeBuilder.CreateStroke(points.Select(p => new Point(p.X - offset.Width, p.Y - offset.Height)))
		};
		session.DrawInk(strokes);
	}

	static (CanvasRenderTarget? offscreen, Size offset) GetCanvasRenderTarget(ICollection<PointF> points, SizeF size, bool scale, float maxLineWidth, Size? canvasSize)
	{
		const int minSize = 1;

		if (points.Count is 0)
		{
			return (null, Size.Zero);
		}

		var minPointX = points.Min(p => p.X) - maxLineWidth;
		var minPointY = points.Min(p => p.Y) - maxLineWidth;
		var drawingWidth = canvasSize?.Width ?? points.Max(p => p.X) - minPointX + maxLineWidth;
		var drawingHeight = canvasSize?.Height ?? points.Max(p => p.Y) - minPointY + maxLineWidth;
		if (drawingWidth < minSize || drawingHeight < minSize)
		{
			return (null, new Size(minPointX, minPointY));
		}

		var device = CanvasDevice.GetSharedDevice();
		var offset = canvasSize is null ? new Size(minPointX, minPointY) : Size.Zero;
		return (new CanvasRenderTarget(device, scale ? size.Width : drawingWidth, scale ? size.Height : drawingHeight, 96), offset);
	}

	static CanvasRenderTarget? GetImageInternal(IList<IDrawingLine> lines, Size size, Paint? background, Size? canvasSize, bool scale = false)
	{
		var points = lines.SelectMany(x => x.Points).ToList();
		var maxLineWidth = lines.Select(x => x.LineWidth).Max();
		var (offscreen, offset) = GetCanvasRenderTarget(points, size, scale, maxLineWidth, canvasSize);
		if (offscreen is null)
		{
			return null;
		}

		using var session = offscreen.CreateDrawingSession();

		var brush = GetCanvasBrush(session, background, offscreen.Size);

		session.FillRectangle(new Rect(0, 0, offscreen.Size.Width, offscreen.Size.Height), brush);

		foreach (var line in lines)
		{
			DrawStrokes(session, line.Points, line.LineColor, line.LineWidth, offset);
		}

		return offscreen;
	}

	static void DrawInk(this CanvasDrawingSession session, IEnumerable<InkStroke> strokes)
	{
		foreach (var stroke in strokes)
		{
			var inkPoints = stroke.GetInkPoints();
			for (var index = 0; index < inkPoints.Count - 1; index++)
			{
				var currentPoint = inkPoints[index].Position;
				var nextPoint = inkPoints[index + 1].Position;
				session.DrawLine(
					new Vector2((float)currentPoint.X, (float)currentPoint.Y),
					new Vector2((float)nextPoint.X, (float)nextPoint.Y),
					stroke.DrawingAttributes.Color, (float)stroke.DrawingAttributes.Size.Width);
			}
		}
	}

	static ICanvasBrush GetCanvasBrush(ICanvasResourceCreator canvasResourceCreator, Paint? brush, Windows.Foundation.Size size)
	{
		switch (brush)
		{
			case SolidPaint solidColorBrush:
				return new CanvasSolidColorBrush(canvasResourceCreator, (solidColorBrush.Color ?? DrawingViewDefaults.BackgroundColor).ToWindowsColor());
			case LinearGradientPaint linearGradientBrush:
				{
					var gradientStops = new CanvasGradientStop[linearGradientBrush.GradientStops.Length];
					for (var index = 0; index < linearGradientBrush.GradientStops.Length; index++)
					{
						var gradientStop = linearGradientBrush.GradientStops[index];
						gradientStops[index] = new CanvasGradientStop(gradientStop.Offset, gradientStop.Color.ToWindowsColor());
					}

					return new CanvasLinearGradientBrush(canvasResourceCreator, gradientStops)
					{
						StartPoint = new Vector2((float)(linearGradientBrush.StartPoint.X * size.Width), (float)(linearGradientBrush.StartPoint.Y * size.Height)),
						EndPoint = new Vector2((float)(linearGradientBrush.EndPoint.X * size.Width), (float)(linearGradientBrush.EndPoint.Y * size.Height))
					};
				}
			case RadialGradientPaint radialGradientBrush:
				{
					var gradientStops = new CanvasGradientStop[radialGradientBrush.GradientStops.Length];
					for (var index = 0; index < radialGradientBrush.GradientStops.Length; index++)
					{
						var gradientStop = radialGradientBrush.GradientStops[index];
						gradientStops[index] = new CanvasGradientStop(gradientStop.Offset, gradientStop.Color.ToWindowsColor());
					}

					return new CanvasRadialGradientBrush(canvasResourceCreator, gradientStops)
					{
						Center = new Vector2((float)(radialGradientBrush.Center.X * size.Width), (float)(radialGradientBrush.Center.Y * size.Height)),
						RadiusX = (float)radialGradientBrush.Radius * size._width,
						RadiusY = (float)radialGradientBrush.Radius * size._height
					};
				}
			default:
				return new CanvasSolidColorBrush(canvasResourceCreator, DrawingViewDefaults.BackgroundColor.ToWindowsColor());
		}
	}
}