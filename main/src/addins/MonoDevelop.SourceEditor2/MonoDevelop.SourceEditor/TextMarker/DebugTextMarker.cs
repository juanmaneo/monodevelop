// DebugTextMarker.cs
//
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
// Copyright (c) 2013 Xamarin Inc. (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Linq;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using MonoDevelop.SourceEditor.Wrappers;
using MonoDevelop.Components;
using MonoDevelop.Debugger;
using MonoDevelop.Ide.Editor;
using Xwt.Drawing;
using System.Collections.Generic;

namespace MonoDevelop.SourceEditor
{
	class DebugIconMarker : MarginMarker
	{
		Image DebugIcon { get; }

		public DebugIconMarker (Image debugIcon)
		{
			DebugIcon = debugIcon;
		}

		public override bool CanDrawForeground (Margin margin)
		{
			return margin is IconMargin;
		}

		public override void DrawForeground (MonoTextEditor editor, Cairo.Context cr, MarginDrawMetrics metrics)
		{
			double size = metrics.Margin.Width;
			double borderLineWidth = cr.LineWidth;

			double x = Math.Floor (metrics.Margin.XOffset - borderLineWidth / 2);
			double y = Math.Floor (metrics.Y + (metrics.Height - size) / 2);

			var deltaX = size / 2 - DebugIcon.Width / 2 + 0.5f;
			var deltaY = size / 2 - DebugIcon.Height / 2 + 0.5f;

			cr.DrawImage (editor, DebugIcon, Math.Round (x + deltaX), Math.Round (y + deltaY));
		}
	}

	class DebugTextMarker : TextSegmentMarker, IChunkMarker
	{
		readonly AmbientColor background;
		readonly ChunkStyle forground;

		public DebugTextMarker (int offset, int length, AmbientColor background, ChunkStyle forground = null)
			: base (offset, length)
		{
			this.forground = forground;
			this.background = background;
		}

		public override void DrawBackground (MonoTextEditor editor, Cairo.Context cr, LineMetrics metrics, int startOffset, int endOffset)
		{

			int markerStart = base.Offset;
			int markerEnd = base.EndOffset;

			if (markerEnd < startOffset || markerStart > endOffset)
				return;

			double @from;
			double to;
			var startXPos = metrics.TextRenderStartPosition;
			var endXPos = metrics.TextRenderEndPosition;
			var y = metrics.LineYRenderStartPosition;
			if (markerStart < startOffset && endOffset < markerEnd) {
				@from = startXPos;
				to = endXPos;
			} else {
				int start = startOffset < markerStart ? markerStart : startOffset;
				int end = endOffset < markerEnd ? endOffset : markerEnd;

				uint curIndex = 0, byteIndex = 0;
				TextViewMargin.TranslateToUTF8Index (metrics.Layout.LineChars, (uint)(start - startOffset), ref curIndex, ref byteIndex);

				int x_pos = metrics.Layout.Layout.IndexToPos ((int)byteIndex).X;

				@from = startXPos + (int)(x_pos / Pango.Scale.PangoScale);

				TextViewMargin.TranslateToUTF8Index (metrics.Layout.LineChars, (uint)(end - startOffset), ref curIndex, ref byteIndex);
				x_pos = metrics.Layout.Layout.IndexToPos ((int)byteIndex).X;

				to = startXPos + (int)(x_pos / Pango.Scale.PangoScale);
			}

			@from = Math.Max (@from, editor.TextViewMargin.XOffset);
			to = Math.Max (to, editor.TextViewMargin.XOffset);
			if (@from < to) {
				cr.SetSourceColor (background.Color);
				cr.RoundedRectangle (@from + 0.5, y + 1.5, to - @from - 1, editor.LineHeight - 2, editor.LineHeight / 4);
				cr.FillPreserve ();

				if (background.HasBorderColor) {
					cr.SetSourceColor (background.BorderColor);
					cr.Stroke ();
				}
			}
		}

		public override ChunkStyle GetStyle (ChunkStyle baseStyle)
		{
			if (baseStyle == null)
				return null;

			var style = new ChunkStyle (baseStyle);
			if (forground != null) {
				style.Foreground = forground.Foreground;
			}
			return style;
		}

		#region IChunkMarker implementation

		void IChunkMarker.TransformChunks (List<Chunk> chunks)
		{
			if (forground == null) {
				return;
			}
			int markerStart = Segment.Offset;
			int markerEnd = Segment.EndOffset;
			for (int i = 0; i < chunks.Count; i++) {
				var chunk = chunks [i];
				if (chunk.EndOffset < markerStart || markerEnd <= chunk.Offset) 
					continue;
				if (chunk.Offset == markerStart && chunk.EndOffset == markerEnd)
					return;
				if (chunk.Offset < markerStart && chunk.EndOffset > markerEnd) {
					var newChunk = new Chunk (chunk.Offset, markerStart - chunk.Offset, chunk.Style);
					chunks.Insert (i, newChunk);
					chunk.Offset += newChunk.Length;
					chunk.Length -= newChunk.Length;
					continue;
				}
			}
		}

		void IChunkMarker.ChangeForeColor (MonoTextEditor editor, Chunk chunk, ref Cairo.Color color)
		{
			if (forground == null) {
				return;
			}
			int markerStart = Segment.Offset;
			int markerEnd = Segment.EndOffset;
			if (chunk.EndOffset <= markerStart || markerEnd <= chunk.Offset) 
				return;
			var bgc = editor.ColorStyle.PlainText.Background;
			color = forground.Foreground;
		}

		#endregion
	}

	abstract class DebugMarkerPair
	{
		public DebugIconMarker IconMarker { get; protected set; }
		public DebugTextMarker TextMarker { get; protected set; }
		private TextDocument document;

		internal void AddTo (TextDocument document, DocumentLine line)
		{
			this.document = document;
			document.AddMarker (line, IconMarker);
			document.AddMarker (TextMarker);
		}

		internal void Remove ()
		{
			if (document != null) {
				document.RemoveMarker (IconMarker);
				document.RemoveMarker (TextMarker);
			}
		}
	}

	class BreakpointTextMarker : DebugMarkerPair
	{
		static readonly Image breakpoint = Image.FromResource (typeof (BreakpointPad), "gutter-breakpoint-15.png");
		static readonly Image tracepoint = Image.FromResource (typeof (BreakpointPad), "gutter-tracepoint-15.png");

		public BreakpointTextMarker (MonoTextEditor editor, int offset, int length, bool isTracepoint)
		{
			IconMarker = new DebugIconMarker (isTracepoint ? tracepoint : breakpoint);
			TextMarker = new DebugTextMarker (offset, length, editor.ColorStyle.BreakpointMarker, editor.ColorStyle.BreakpointText);
		}
	}

	class DisabledBreakpointTextMarker : DebugMarkerPair
	{
		static readonly Image breakpoint = Image.FromResource (typeof (BreakpointPad), "gutter-breakpoint-disabled-15.png");
		static readonly Image tracepoint = Image.FromResource (typeof (BreakpointPad), "gutter-tracepoint-disabled-15.png");

		public DisabledBreakpointTextMarker (MonoTextEditor editor, int offset, int length, bool isTracepoint)
		{
			IconMarker = new DebugIconMarker (isTracepoint ? tracepoint : breakpoint);
			TextMarker = new DebugTextMarker (offset, length, editor.ColorStyle.BreakpointMarkerDisabled);
		}
	}

	class InvalidBreakpointTextMarker : DebugMarkerPair
	{
		static readonly Image breakpoint = Image.FromResource (typeof (BreakpointPad), "gutter-breakpoint-invalid-15.png");
		static readonly Image tracepoint = Image.FromResource (typeof (BreakpointPad), "gutter-tracepoint-invalid-15.png");

		public InvalidBreakpointTextMarker (MonoTextEditor editor, int offset, int length, bool isTracepoint)
		{
			IconMarker = new DebugIconMarker (isTracepoint ? tracepoint : breakpoint);
			TextMarker = new DebugTextMarker (offset, length, editor.ColorStyle.BreakpointMarkerInvalid);
		}
	}

	class DebugStackLineTextMarker : DebugMarkerPair
	{
		static readonly Image stackLine = Image.FromResource (typeof (BreakpointPad), "gutter-stack-15.png");

		public DebugStackLineTextMarker (MonoTextEditor editor, int offset, int length)
		{
			IconMarker = new DebugIconMarker (stackLine);
			TextMarker = new DebugTextMarker (offset, length, editor.ColorStyle.DebuggerStackLineMarker, editor.ColorStyle.DebuggerStackLine);
		}
	}

	class CurrentDebugLineTextMarker : DebugMarkerPair, ICurrentDebugLineTextMarker
	{
		static readonly Image currentLine = Image.FromResource (typeof (BreakpointPad), "gutter-execution-15.png");

		public CurrentDebugLineTextMarker (MonoTextEditor editor, int offset, int length)
		{
			IconMarker = new DebugIconMarker (currentLine);
			TextMarker = new DebugTextMarker (offset, length, editor.ColorStyle.DebuggerCurrentLineMarker, editor.ColorStyle.DebuggerCurrentLine);
		}

		public bool IsVisible { get { return IconMarker.IsVisible; } set { IconMarker.IsVisible = value; } }

		IDocumentLine ITextLineMarker.Line { get { return new DocumentLineWrapper (IconMarker.LineSegment); } }

		public object Tag { get { return IconMarker.Tag; } set { IconMarker.Tag = value; } }
	}

}
