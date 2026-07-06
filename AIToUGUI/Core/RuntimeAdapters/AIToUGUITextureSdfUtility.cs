using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

namespace AIToUGUI
{
    internal static class AIToUGUITextureSdfUtility
    {
        // 图片/Sprite gooey 的前提是先把源轮廓转成 SDF stamp。
        // 否则 texture 只能做普通 alpha 混合，无法和 bridge/head 做平滑 union。
        private readonly struct SdfCacheKey : IEquatable<SdfCacheKey>
        {
            public SdfCacheKey(int instanceId, int x, int y, int width, int height)
            {
                InstanceId = instanceId;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int InstanceId { get; }
            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }

            public bool Equals(SdfCacheKey other)
            {
                return InstanceId == other.InstanceId &&
                       X == other.X &&
                       Y == other.Y &&
                       Width == other.Width &&
                       Height == other.Height;
            }

            public override bool Equals(object obj)
            {
                return obj is SdfCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = InstanceId;
                    hashCode = (hashCode * 397) ^ X;
                    hashCode = (hashCode * 397) ^ Y;
                    hashCode = (hashCode * 397) ^ Width;
                    hashCode = (hashCode * 397) ^ Height;
                    return hashCode;
                }
            }
        }

        private const float AlphaThreshold = 0.1f;
        private const float InfiniteDistance = 1e20f;
        private static readonly Dictionary<SdfCacheKey, Texture2D> s_cache = new Dictionary<SdfCacheKey, Texture2D>();

        public static Texture2D GetOrCreate(Texture texture)
        {
            if (texture == null)
            {
                return null;
            }

            var width = Mathf.Max(1, texture.width);
            var height = Mathf.Max(1, texture.height);
            var key = new SdfCacheKey(texture.GetInstanceID(), 0, 0, width, height);
            if (s_cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // 先抓一份可读区域，再转 SDF 并缓存。
            // 运行时频繁切 tab 时不能重复做 CPU 距离场计算，否则代价过高。
            var readable = CaptureRegion(texture, width, height, new Rect(0f, 0f, 1f, 1f));
            var sdf = BuildSdfTexture(readable, texture.name + "_SDF");
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(readable);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }

            s_cache[key] = sdf;
            return sdf;
        }

        public static Texture2D GetOrCreate(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return null;
            }

            var rect = sprite.rect;
            var key = new SdfCacheKey(
                sprite.texture.GetInstanceID(),
                Mathf.RoundToInt(rect.x),
                Mathf.RoundToInt(rect.y),
                Mathf.RoundToInt(rect.width),
                Mathf.RoundToInt(rect.height));

            if (s_cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // Sprite 必须按 atlas 中自己的 uv 区域截取，不能直接拿整张 texture。
            // 否则图集子图会把邻近像素也带进 SDF，边缘会脏，轮廓也会错。
            var uv = DataUtility.GetOuterUV(sprite);
            var uvRect = Rect.MinMaxRect(
                Mathf.Min(uv.x, uv.z),
                Mathf.Min(uv.y, uv.w),
                Mathf.Max(uv.x, uv.z),
                Mathf.Max(uv.y, uv.w));

            var width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            var readable = CaptureRegion(sprite.texture, width, height, uvRect);
            var sdf = BuildSdfTexture(readable, sprite.name + "_SDF");
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(readable);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }

            s_cache[key] = sdf;
            return sdf;
        }

        private static Texture2D CaptureRegion(Texture source, int width, int height, Rect uvRect)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt, new Vector2(uvRect.width, uvRect.height), new Vector2(uvRect.x, uvRect.y));
                RenderTexture.active = rt;

                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = source.name + "_ReadableRegion",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D BuildSdfTexture(Texture2D source, string name)
        {
            var width = source.width;
            var height = source.height;
            var pixels = source.GetPixels32();
            var inside = new bool[width * height];

            var hasInside = false;
            var hasOutside = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                var isInside = pixels[i].a / 255f > AlphaThreshold;
                inside[i] = isInside;
                hasInside |= isInside;
                hasOutside |= !isInside;
            }

            var sdfPixels = new Color32[pixels.Length];
            if (!hasInside || !hasOutside)
            {
                var value = hasInside ? (byte)0 : (byte)255;
                for (var i = 0; i < sdfPixels.Length; i++)
                {
                    sdfPixels[i] = new Color32(value, value, value, 255);
                }
            }
            else
            {
                // inside/outside 两次距离变换后做 signed distance 编码。
                // 这里输出 R8 即可，后续 shader 只关心单通道的轮廓距离。
                var insideDistance = ComputeDistanceTransform(inside, width, height);
                var outsideMask = new bool[inside.Length];
                for (var i = 0; i < inside.Length; i++)
                {
                    outsideMask[i] = !inside[i];
                }

                var outsideDistance = ComputeDistanceTransform(outsideMask, width, height);
                var maxDistance = Mathf.Max(1f, Mathf.Sqrt(width * width + height * height) * 0.5f);

                for (var i = 0; i < sdfPixels.Length; i++)
                {
                    var signedDistance = Mathf.Sqrt(insideDistance[i]) - Mathf.Sqrt(outsideDistance[i]);
                    var encoded = Mathf.Clamp01((signedDistance / maxDistance + 1f) * 0.5f);
                    var channel = (byte)Mathf.RoundToInt(encoded * 255f);
                    sdfPixels[i] = new Color32(channel, channel, channel, 255);
                }
            }

            var sdf = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            sdf.SetPixels32(sdfPixels);
            sdf.Apply(false, true);
            return sdf;
        }

        private static float[] ComputeDistanceTransform(bool[] featureMask, int width, int height)
        {
            var grid = new float[width * height];
            var column = new float[Mathf.Max(width, height)];
            var transform = new float[Mathf.Max(width, height)];
            var positions = new int[Mathf.Max(width, height)];
            var boundaries = new float[Mathf.Max(width, height) + 1];

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    column[y] = featureMask[y * width + x] ? 0f : InfiniteDistance;
                }

                Transform1D(column, height, transform, positions, boundaries);
                for (var y = 0; y < height; y++)
                {
                    grid[y * width + x] = transform[y];
                }
            }

            var row = new float[Mathf.Max(width, height)];
            var result = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    row[x] = grid[y * width + x];
                }

                Transform1D(row, width, transform, positions, boundaries);
                for (var x = 0; x < width; x++)
                {
                    result[y * width + x] = transform[x];
                }
            }

            return result;
        }

        private static void Transform1D(float[] values, int length, float[] output, int[] positions, float[] boundaries)
        {
            var k = 0;
            positions[0] = 0;
            boundaries[0] = float.NegativeInfinity;
            boundaries[1] = float.PositiveInfinity;

            for (var q = 1; q < length; q++)
            {
                float intersection;
                do
                {
                    var previous = positions[k];
                    intersection = ((values[q] + q * q) - (values[previous] + previous * previous)) / (2f * (q - previous));
                    if (intersection <= boundaries[k])
                    {
                        k--;
                    }
                    else
                    {
                        break;
                    }
                } while (k >= 0);

                k++;
                positions[k] = q;
                boundaries[k] = intersection;
                boundaries[k + 1] = float.PositiveInfinity;
            }

            k = 0;
            for (var q = 0; q < length; q++)
            {
                while (boundaries[k + 1] < q)
                {
                    k++;
                }

                var distance = q - positions[k];
                output[q] = distance * distance + values[positions[k]];
            }
        }
    }
}
