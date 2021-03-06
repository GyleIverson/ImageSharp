// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.InteropServices;

using SixLabors.ImageSharp.Advanced.ParallelUtils;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Primitives;
using SixLabors.Primitives;

namespace SixLabors.ImageSharp.Processing.Processors.Convolution
{
    /// <summary>
    /// Defines a processor that uses two one-dimensional matrices to perform two-pass convolution against an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    internal class Convolution2PassProcessor<TPixel> : ImageProcessor<TPixel>
        where TPixel : struct, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Convolution2PassProcessor{TPixel}"/> class.
        /// </summary>
        /// <param name="kernelX">The horizontal gradient operator.</param>
        /// <param name="kernelY">The vertical gradient operator.</param>
        /// <param name="preserveAlpha">Whether the convolution filter is applied to alpha as well as the color channels.</param>
        /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
        /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
        public Convolution2PassProcessor(
            in DenseMatrix<float> kernelX,
            in DenseMatrix<float> kernelY,
            bool preserveAlpha,
            Image<TPixel> source,
            Rectangle sourceRectangle)
            : base(source, sourceRectangle)
        {
            this.KernelX = kernelX;
            this.KernelY = kernelY;
            this.PreserveAlpha = preserveAlpha;
        }

        /// <summary>
        /// Gets the horizontal gradient operator.
        /// </summary>
        public DenseMatrix<float> KernelX { get; }

        /// <summary>
        /// Gets the vertical gradient operator.
        /// </summary>
        public DenseMatrix<float> KernelY { get; }

        /// <summary>
        /// Gets a value indicating whether the convolution filter is applied to alpha as well as the color channels.
        /// </summary>
        public bool PreserveAlpha { get; }

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            using (Buffer2D<TPixel> firstPassPixels = this.Configuration.MemoryAllocator.Allocate2D<TPixel>(source.Size()))
            {
                var interest = Rectangle.Intersect(this.SourceRectangle, source.Bounds());
                this.ApplyConvolution(firstPassPixels, source.PixelBuffer, interest, this.KernelX, this.Configuration);
                this.ApplyConvolution(source.PixelBuffer, firstPassPixels, interest, this.KernelY, this.Configuration);
            }
        }

        /// <summary>
        /// Applies the process to the specified portion of the specified <see cref="ImageFrame{TPixel}"/> at the specified location
        /// and with the specified size.
        /// </summary>
        /// <param name="targetPixels">The target pixels to apply the process to.</param>
        /// <param name="sourcePixels">The source pixels. Cannot be null.</param>
        /// <param name="sourceRectangle">
        /// The <see cref="Rectangle"/> structure that specifies the portion of the image object to draw.
        /// </param>
        /// <param name="kernel">The kernel operator.</param>
        /// <param name="configuration">The <see cref="Configuration"/></param>
        private void ApplyConvolution(
            Buffer2D<TPixel> targetPixels,
            Buffer2D<TPixel> sourcePixels,
            Rectangle sourceRectangle,
            in DenseMatrix<float> kernel,
            Configuration configuration)
        {
            DenseMatrix<float> matrix = kernel;
            bool preserveAlpha = this.PreserveAlpha;

            int startY = sourceRectangle.Y;
            int endY = sourceRectangle.Bottom;
            int startX = sourceRectangle.X;
            int endX = sourceRectangle.Right;
            int maxY = endY - 1;
            int maxX = endX - 1;

            var workingRectangle = Rectangle.FromLTRB(startX, startY, endX, endY);
            int width = workingRectangle.Width;

            ParallelHelper.IterateRowsWithTempBuffer<Vector4>(
                workingRectangle,
                configuration,
                (rows, vectorBuffer) =>
                    {
                        Span<Vector4> vectorSpan = vectorBuffer.Span;
                        int length = vectorSpan.Length;
                        ref Vector4 vectorSpanRef = ref MemoryMarshal.GetReference(vectorSpan);

                        for (int y = rows.Min; y < rows.Max; y++)
                        {
                            Span<TPixel> targetRowSpan = targetPixels.GetRowSpan(y).Slice(startX);
                            PixelOperations<TPixel>.Instance.ToVector4(configuration, targetRowSpan.Slice(0, length), vectorSpan);

                            if (preserveAlpha)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    DenseMatrixUtils.Convolve3(
                                        in matrix,
                                        sourcePixels,
                                        ref vectorSpanRef,
                                        y,
                                        x,
                                        startY,
                                        maxY,
                                        startX,
                                        maxX);
                                }
                            }
                            else
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    DenseMatrixUtils.Convolve4(
                                        in matrix,
                                        sourcePixels,
                                        ref vectorSpanRef,
                                        y,
                                        x,
                                        startY,
                                        maxY,
                                        startX,
                                        maxX);
                                }
                            }

                            PixelOperations<TPixel>.Instance.FromVector4Destructive(configuration, vectorSpan, targetRowSpan);
                        }
                    });
        }
    }
}
