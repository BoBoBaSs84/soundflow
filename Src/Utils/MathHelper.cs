﻿using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SoundFlow.Utils;

/// <summary>
///     Helper methods for common math operations.
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// Computes the Inverse Fast Fourier Transform (IFFT) of a complex array.
    /// </summary>
    /// <param name="data">The complex data array.</param>
    public static void InverseFft(Complex[] data)
    {
        // Conjugate the complex data
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = Complex.Conjugate(data[i]);
        }

        // Perform FFT
        Fft(data);

        // Conjugate and scale the result
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = Complex.Conjugate(data[i]);
        }
    }

    /// <summary>
    /// Computes the Fast Fourier Transform (FFT) of a complex array using SIMD acceleration with fallback to a scalar implementation.
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    public static void Fft(Complex[] data)
    {
        var n = data.Length;
        if (n <= 1) return;

        if (Avx.IsSupported && n >= 8) // Use AVX for larger arrays
            FftAvx(data);
        else if (Sse2.IsSupported && n >= 4) // Use SSE2 for smaller arrays
            FftSse2(data);
        else // Fallback to scalar implementation
            FftScalar(data);
    }

    /// <summary>
    /// Scalar implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static void FftScalar(Complex[] data)
    {
        var n = data.Length;
        if (n <= 1) return;

        // Separate even and odd elements
        var even = new Complex[n / 2];
        var odd = new Complex[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            even[i] = data[2 * i];
            odd[i] = data[2 * i + 1];
        }

        // Recursive FFT on even and odd parts
        FftScalar(even);
        FftScalar(odd);

        // Combine
        for (var k = 0; k < n / 2; k++)
        {
            var t = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * k / n) * odd[k];
            data[k] = even[k] + t;
            data[k + n / 2] = even[k] - t;
        }
    }

    /// <summary>
    /// SSE2-accelerated implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length and at least 4.</param>
    private static unsafe void FftSse2(Complex[] data)
    {
        var n = data.Length;

        // Bit-reverse the data
        BitReverse(data);

        // Cooley-Tukey FFT algorithm with SSE2
        for (var s = 1; s <= Math.Log(n, 2); s++)
        {
            var m = 1 << s;
            var m2 = m >> 1;
            var wm = Vector128.Create(Complex.FromPolarCoordinates(1.0, -Math.PI / m2).Real,
                Complex.FromPolarCoordinates(1.0, -Math.PI / m2).Imaginary);

            for (var k = 0; k < n; k += m)
            {
                var w = Vector128.Create(1.0, 0.0);
                for (var j = 0; j < m2; j += 2)
                {
                    fixed (Complex* pData = &data[0])
                    {
                        // Load even and odd elements
                        var even1 = Sse2.LoadVector128((double*)(pData + k + j));
                        var odd1 = Sse2.LoadVector128((double*)(pData + k + j + m2));

                        var even2 = Sse2.LoadVector128((double*)(pData + k + j + 2));
                        var odd2 = Sse2.LoadVector128((double*)(pData + k + j + m2 + 2));

                        // Calculate twiddle factors
                        var twiddle1 = MultiplyComplexSse2(odd1, w);

                        // Update w
                        w = MultiplyComplexSse2(w, wm);
                        var twiddle2 = MultiplyComplexSse2(odd2, w);
                        w = MultiplyComplexSse2(w, wm);

                        // Butterfly operations
                        Sse2.Store((double*)(pData + k + j), Sse2.Add(even1, twiddle1));
                        Sse2.Store((double*)(pData + k + j + m2), Sse2.Subtract(even1, twiddle1));

                        Sse2.Store((double*)(pData + k + j + 2), Sse2.Add(even2, twiddle2));
                        Sse2.Store((double*)(pData + k + j + m2 + 2), Sse2.Subtract(even2, twiddle2));
                    }
                }
            }
        }
    }

    /// <summary>
    /// AVX-accelerated implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length and at least 8.</param>
    private static unsafe void FftAvx(Complex[] data)
    {
        var n = data.Length;

        // Bit-reverse the data
        BitReverse(data);

        // Cooley-Tukey FFT algorithm with AVX
        for (var s = 1; s <= Math.Log(n, 2); s++)
        {
            var m = 1 << s;
            var m2 = m >> 1;
            var wm = Vector256.Create(
                Complex.FromPolarCoordinates(1.0, -Math.PI / m2).Real,
                Complex.FromPolarCoordinates(1.0, -Math.PI / m2).Imaginary,
                Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI / m2).Real,
                Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI / m2).Imaginary
            );

            for (var k = 0; k < n; k += m)
            {
                var w = Vector256.Create(1.0, 0.0, 1.0, 0.0);
                for (var j = 0; j < m2; j += 2)
                {
                    fixed (Complex* pData = &data[0])
                    {
                        // Load even and odd elements
                        var even = Avx.LoadVector256((double*)(pData + k + j));
                        var odd = Avx.LoadVector256((double*)(pData + k + j + m2));

                        // Calculate twiddle factors
                        var twiddle = MultiplyComplexAvx(odd, w);

                        // Update w
                        w = MultiplyComplexAvx(w, wm);

                        // Butterfly operations
                        Avx.Store((double*)(pData + k + j), Avx.Add(even, twiddle));
                        Avx.Store((double*)(pData + k + j + m2), Avx.Subtract(even, twiddle));
                    }
                }

                // Handle remaining elements if m2 is not a multiple of 2
                for (var j = (m2 / 2) * 2; j < m2; ++j)
                {
                    // Scalar version of the FFT butterfly
                    var t = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * j / m) * data[k + j + m2];
                    data[k + j + m2] = data[k + j] - t;
                    data[k + j] += t;
                }
            }
        }
    }

    /// <summary>
    /// Bit-reverses the order of elements in a complex array.
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static void BitReverse(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) > 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (data[j], data[i]) = (data[i], data[j]);
            }
        }
    }

    /// <summary>
    /// Multiplies two complex numbers represented as Vector128.
    /// </summary>
    /// <param name="a">The first complex number (real, imaginary).</param>
    /// <param name="b">The second complex number (real, imaginary).</param>
    /// <returns>The result of complex multiplication (real, imaginary).</returns>
    private static Vector128<double> MultiplyComplexSse2(Vector128<double> a, Vector128<double> b)
    {
        // (a.Real * b.Real - a.Imaginary * b.Imaginary, a.Real * b.Imaginary + a.Imaginary * b.Real)
        var real = Sse2.Multiply(a, b);
        var imaginary =
            Sse2.Multiply(Sse2.Shuffle(a, a, 0b_01_00_01_00),
                Sse2.Shuffle(b, b,
                    0b_01_00_01_00)); // [a.Imaginary, a.Real, a.Imaginary, a.Real] * [b.Imaginary, b.Real, b.Imaginary, b.Real]

        // Negate the second element in imaginary
        var sign = Vector128.Create(-1.0, 1.0);
        imaginary = Sse2.Multiply(imaginary, sign);

        return Sse2.Add(real,
            Sse2.Shuffle(imaginary, imaginary,
                0b_01_00_01_00)); // [real.Real - imaginary.Imaginary, real.Imaginary + imaginary.Real]
    }

    /// <summary>
    /// Multiplies two complex numbers represented as Vector256.
    /// </summary>
    /// <param name="a">The first complex number (real, imaginary, real, imaginary).</param>
    /// <param name="b">The second complex number (real, imaginary, real, imaginary).</param>
    /// <returns>The result of complex multiplication (real, imaginary, real, imaginary).</returns>
    private static Vector256<double> MultiplyComplexAvx(Vector256<double> a, Vector256<double> b)
    {
        // (a.Real * b.Real - a.Imaginary * b.Imaginary, a.Real * b.Imaginary + a.Imaginary * b.Real)
        var real = Avx.Multiply(a, b);
        var imaginary = Avx.Multiply(Avx.Shuffle(a, a, 0b_01_00_01_00), Avx.Shuffle(b, b, 0b_01_00_01_00));

        // Negate the second and fourth elements in imaginary
        var sign = Vector256.Create(-1.0, 1.0, -1.0, 1.0);
        imaginary = Avx.Multiply(imaginary, sign);

        return Avx.Add(real, Avx.Shuffle(imaginary, imaginary, 0b_01_00_01_00));
    }

    /// <summary>
    /// Generates a Hamming window of a specified size using SIMD acceleration with fallback to a scalar implementation.
    /// </summary>
    /// <param name="size">The size of the Hamming window.</param>
    /// <returns>The Hamming window array.</returns>
    public static float[] HammingWindow(int size)
    {
        if (Avx.IsSupported && size >= Vector256<float>.Count)
            return HammingWindowAvx(size);

        if (Sse.IsSupported && size >= Vector128<float>.Count)
            return HammingWindowSse(size);

        return HammingWindowScalar(size);
    }

    /// <summary>
    /// Generates a Hamming window using a scalar implementation.
    /// </summary>
    /// <param name="size">The size of the Hamming window.</param>
    /// <returns>The Hamming window array.</returns>
    private static float[] HammingWindowScalar(int size)
    {
        var window = new float[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos((2 * MathF.PI * i) / (size - 1));
        }

        return window;
    }

    /// <summary>
    /// Generates a Hamming window using SSE acceleration.
    /// </summary>
    /// <param name="size">The size of the Hamming window.</param>
    /// <returns>The Hamming window array.</returns>
    private static unsafe float[] HammingWindowSse(int size)
    {
        var window = new float[size];
        var vectorSize = Vector128<float>.Count;
        var remainder = size % vectorSize;

        fixed (float* pWindow = window)
        {
            // Precompute constants
            var vConstA = Vector128.Create(0.54f);
            var vConstB = Vector128.Create(0.46f);
            var vTwoPi = Vector128.Create(2.0f * MathF.PI / (size - 1));

            // Process in chunks of vectorSize
            for (var i = 0; i < size - remainder; i += vectorSize)
            {
                // Create a vector of indices (i, i+1, i+2, i+3)
                var vIndices = Vector128.Create((float)i, i + 1, i + 2, i + 3);

                // Calculate the cosine argument: (2 * PI * i) / (size - 1)
                var vCosArg = Sse.Multiply(vTwoPi, vIndices);

                // Calculate the cosine value using a fast approximation (could be improved)
                var vCos = FastCosineSse(vCosArg);

                // Calculate the Hamming window value: 0.54 - 0.46 * cos(arg)
                var vResult = Sse.Subtract(vConstA, Sse.Multiply(vConstB, vCos));

                // Store the result
                Sse.Store(pWindow + i, vResult);
            }

            // Handle the remaining elements
            for (var i = size - remainder; i < size; i++)
            {
                window[i] = 0.54f - 0.46f * MathF.Cos((2 * MathF.PI * i) / (size - 1));
            }
        }

        return window;
    }

    /// <summary>
    /// Generates a Hamming window using AVX acceleration.
    /// </summary>
    /// <param name="size">The size of the Hamming window.</param>
    /// <returns>The Hamming window array.</returns>
    private static unsafe float[] HammingWindowAvx(int size)
    {
        var window = new float[size];
        var vectorSize = Vector256<float>.Count;
        var remainder = size % vectorSize;

        fixed (float* pWindow = window)
        {
            // Precompute constants
            var vConstA = Vector256.Create(0.54f);
            var vConstB = Vector256.Create(0.46f);
            var vTwoPi = Vector256.Create(2.0f * MathF.PI / (size - 1));

            // Process in chunks of vectorSize
            for (var i = 0; i < size - remainder; i += vectorSize)
            {
                // Create a vector of indices (i, i+1, ..., i+7)
                var vIndices = Vector256.Create((float)i, i + 1, i + 2, i + 3, i + 4, i + 5, i + 6, i + 7);

                // Calculate the cosine argument: (2 * PI * i) / (size - 1)
                var vCosArg = Avx.Multiply(vTwoPi, vIndices);

                // Calculate the cosine value using a fast approximation (could be improved)
                var vCos = FastCosineAvx(vCosArg);

                // Calculate the Hamming window value: 0.54 - 0.46 * cos(arg)
                var vResult = Avx.Subtract(vConstA, Avx.Multiply(vConstB, vCos));

                // Store the result
                Avx.Store(pWindow + i, vResult);
            }

            // Handle the remaining elements
            for (var i = size - remainder; i < size; i++)
            {
                window[i] = 0.54f - 0.46f * MathF.Cos((2 * MathF.PI * i) / (size - 1));
            }
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window of a specified size using SIMD acceleration with fallback to a scalar implementation.
    /// </summary>
    /// <param name="size">The size of the Hanning window.</param>
    /// <returns>The Hanning window array.</returns>
    public static float[] HanningWindow(int size)
    {
        if (Avx.IsSupported && size >= Vector256<float>.Count)
            return HanningWindowAvx(size);

        if (Sse.IsSupported && size >= Vector128<float>.Count)
            return HanningWindowSse(size);

        return HanningWindowScalar(size);
    }

    /// <summary>
    /// Generates a Hanning window using a scalar implementation.
    /// </summary>
    /// <param name="size">The size of the Hanning window.</param>
    /// <returns>The Hanning window array.</returns>
    private static float[] HanningWindowScalar(int size)
    {
        var window = new float[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1.0f - MathF.Cos((2 * MathF.PI * i) / (size - 1)));
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window using SSE acceleration.
    /// </summary>
    /// <param name="size">The size of the Hanning window.</param>
    /// <returns>The Hanning window array.</returns>
    private static unsafe float[] HanningWindowSse(int size)
    {
        var window = new float[size];
        var vectorSize = Vector128<float>.Count;
        var remainder = size % vectorSize;

        fixed (float* pWindow = window)
        {
            var vConstA = Vector128.Create(0.5f);
            var vConstB = Vector128.Create(0.5f);
            var vTwoPi = Vector128.Create(2.0f * MathF.PI / (size - 1));

            for (var i = 0; i < size - remainder; i += vectorSize)
            {
                var vIndices = Vector128.Create((float)i, i + 1, i + 2, i + 3);
                var vCosArg = Sse.Multiply(vTwoPi, vIndices);
                var vCos = FastCosineSse(vCosArg);
                var vResult = Sse.Subtract(vConstA, Sse.Multiply(vConstB, vCos));
                Sse.Store(pWindow + i, vResult);
            }

            // Handle remaining elements
            for (var i = size - remainder; i < size; i++)
            {
                window[i] = 0.5f * (1.0f - MathF.Cos((2 * MathF.PI * i) / (size - 1)));
            }
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window using AVX acceleration.
    /// </summary>
    /// <param name="size">The size of the Hanning window.</param>
    /// <returns>The Hanning window array.</returns>
    private static unsafe float[] HanningWindowAvx(int size)
    {
        var window = new float[size];
        var vectorSize = Vector256<float>.Count;
        var remainder = size % vectorSize;

        fixed (float* pWindow = window)
        {
            var vConstA = Vector256.Create(0.5f);
            var vConstB = Vector256.Create(0.5f);
            var vTwoPi = Vector256.Create(2.0f * MathF.PI / (size - 1));

            for (var i = 0; i < size - remainder; i += vectorSize)
            {
                var vIndices = Vector256.Create((float)i, i + 1, i + 2, i + 3,
                    i + 4, i + 5, i + 6, i + 7);
                var vCosArg = Avx.Multiply(vTwoPi, vIndices);
                var vCos = FastCosineAvx(vCosArg);
                var vResult = Avx.Subtract(vConstA, Avx.Multiply(vConstB, vCos));
                Avx.Store(pWindow + i, vResult);
            }

            // Handle remaining elements
            for (var i = size - remainder; i < size; i++)
            {
                window[i] = 0.5f * (1.0f - MathF.Cos((2 * MathF.PI * i) / (size - 1)));
            }
        }

        return window;
    }

    /// <summary>
    /// Approximates the cosine of a vector using SSE instructions.
    /// Placeholder for now, I need to implement a more accurate approximation.
    /// </summary>
    /// <param name="x">The input vector.</param>
    /// <returns>The approximated cosine of the input vector.</returns>
    private static Vector128<float> FastCosineSse(Vector128<float> x)
    {
        // Simple polynomial approximation (for demonstration - needs improvement)
        // cos(x) ≈ 1 - x^2/2 + x^4/24
        var x2 = Sse.Multiply(x, x);
        var x4 = Sse.Multiply(x2, x2);
        var term2 = Sse.Multiply(x2, Vector128.Create(1f / 2f));
        var term4 = Sse.Multiply(x4, Vector128.Create(1f / 24f));

        return Sse.Subtract(Vector128.Create(1.0f), Sse.Add(term2, term4));
    }

    /// <summary>
    /// Approximates the cosine of a vector using AVX instructions.
    /// Placeholder for now, I need to implement a more accurate approximation.
    /// </summary>
    /// <param name="x">The input vector.</param>
    /// <returns>The approximated cosine of the input vector.</returns>
    private static Vector256<float> FastCosineAvx(Vector256<float> x)
    {
        // Simple polynomial approximation (for demonstration - needs improvement)
        // cos(x) ≈ 1 - x^2/2 + x^4/24
        var x2 = Avx.Multiply(x, x);
        var x4 = Avx.Multiply(x2, x2);
        var term2 = Avx.Multiply(x2, Vector256.Create(1f / 2f));
        var term4 = Avx.Multiply(x4, Vector256.Create(1f / 24f));

        return Avx.Subtract(Vector256.Create(1.0f), Avx.Add(term2, term4));
    }
}