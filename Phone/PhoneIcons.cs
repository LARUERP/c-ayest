using System;
using UnityEngine;

namespace FusionDarkRP.Telephone;

public static class PhoneIcons
{
    private static bool Line(
        float x, float y,
        float ax, float ay,
        float bx, float by,
        float w = 2.8f)
    {
        float dx = bx - ax;
        float dy = by - ay;

        float t = Math.Max(
            0f,
            Math.Min(
                1f,
                ((x - ax) * dx + (y - ay) * dy) /
                (dx * dx + dy * dy)));

        float px = x - ax - t * dx;
        float py = y - ay - t * dy;

        return px * px + py * py < w * w;
    }

    public static Sprite Create(string key)
    {
        Texture2D texture = new Texture2D(
            64, 64, TextureFormat.RGBA32, false);

        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float num = x;
                float num2 = y;

                float distance = (float)Math.Sqrt(
                    (num - 32f) * (num - 32f) +
                    (num2 - 32f) * (num2 - 32f));

                bool visible = false;

                switch (key)
                {
                    case "windy":
                        visible =
                            Line(num, num2, 8f, 39f, 44f, 39f) ||
                            Line(num, num2, 44f, 39f, 51f, 45f) ||
                            Line(num, num2, 51f, 45f, 48f, 53f) ||
                            Line(num, num2, 48f, 53f, 40f, 53f) ||
                            Line(num, num2, 8f, 27f, 51f, 27f) ||
                            Line(num, num2, 51f, 27f, 56f, 34f) ||
                            Line(num, num2, 56f, 34f, 51f, 39f) ||
                            Line(num, num2, 8f, 16f, 34f, 16f) ||
                            Line(num, num2, 34f, 16f, 41f, 9f) ||
                            Line(num, num2, 41f, 9f, 34f, 5f);
                        break;

                    case "camera":
                        visible =
                            Line(num, num2, 10f, 16f, 54f, 16f) ||
                            Line(num, num2, 54f, 16f, 54f, 45f) ||
                            Line(num, num2, 54f, 45f, 10f, 45f) ||
                            Line(num, num2, 10f, 45f, 10f, 16f) ||
                            Line(num, num2, 23f, 45f, 27f, 52f) ||
                            Line(num, num2, 27f, 52f, 39f, 52f) ||
                            Line(num, num2, 39f, 52f, 43f, 45f) ||
                            Math.Abs((float)Math.Sqrt(
                                (num - 32f) * (num - 32f) +
                                (num2 - 30f) * (num2 - 30f)) - 10f) < 2.5f;
                        break;

                    case "gallery":
                        visible =
                            Line(num, num2, 15f, 10f, 49f, 10f) ||
                            Line(num, num2, 49f, 10f, 49f, 43f) ||
                            Line(num, num2, 49f, 43f, 39f, 53f) ||
                            Line(num, num2, 39f, 53f, 15f, 53f) ||
                            Line(num, num2, 15f, 53f, 15f, 10f) ||
                            Line(num, num2, 39f, 53f, 39f, 43f) ||
                            Line(num, num2, 39f, 43f, 49f, 43f) ||
                            Line(num, num2, 23f, 21f, 41f, 21f) ||
                            Line(num, num2, 23f, 31f, 41f, 31f);
                        break;

                    case "send":
                        visible =
                            Line(num, num2, 9f, 12f, 55f, 32f) ||
                            Line(num, num2, 55f, 32f, 9f, 52f) ||
                            Line(num, num2, 9f, 52f, 19f, 32f) ||
                            Line(num, num2, 19f, 32f, 9f, 12f) ||
                            Line(num, num2, 19f, 32f, 48f, 32f);
                        break;

                    case "clip":
                        visible =
                            Line(num, num2, 22f, 18f, 43f, 39f) ||
                            Line(num, num2, 43f, 39f, 43f, 47f) ||
                            Line(num, num2, 43f, 47f, 35f, 51f) ||
                            Line(num, num2, 35f, 51f, 12f, 28f) ||
                            Line(num, num2, 12f, 28f, 12f, 18f) ||
                            Line(num, num2, 12f, 18f, 22f, 8f) ||
                            Line(num, num2, 22f, 8f, 31f, 8f) ||
                            Line(num, num2, 31f, 8f, 51f, 28f) ||
                            Line(num, num2, 22f, 18f, 26f, 14f) ||
                            Line(num, num2, 26f, 14f, 46f, 34f);
                        break;

                    case "contacts":
                        visible =
                            (num - 32f) * (num - 32f) +
                            (num2 - 43f) * (num2 - 43f) < 81f ||
                            (num2 > 12f && num2 < 29f &&
                            (num - 32f) * (num - 32f) +
                            (num2 - 12f) * (num2 - 12f) < 300f);
                        break;

                    case "messages":
                        visible =
                            Line(num, num2, 12f, 45f, 52f, 45f) ||
                            Line(num, num2, 52f, 45f, 52f, 23f) ||
                            Line(num, num2, 52f, 23f, 27f, 23f) ||
                            Line(num, num2, 27f, 23f, 16f, 13f) ||
                            Line(num, num2, 16f, 13f, 16f, 23f) ||
                            Line(num, num2, 16f, 23f, 12f, 23f) ||
                            Line(num, num2, 12f, 23f, 12f, 45f) ||
                            (num2 > 31f && num2 < 36f &&
                            (Math.Abs(num - 23f) < 2f ||
                             Math.Abs(num - 32f) < 2f ||
                             Math.Abs(num - 41f) < 2f));
                        break;

                    case "microphone":
                        visible =
                            Line(num, num2, 26f, 44f, 26f, 28f) ||
                            Line(num, num2, 26f, 44f, 38f, 44f) ||
                            Line(num, num2, 38f, 44f, 38f, 28f) ||
                            Line(num, num2, 38f, 28f, 26f, 28f) ||
                            Line(num, num2, 19f, 31f, 19f, 22f) ||
                            Line(num, num2, 19f, 22f, 32f, 17f) ||
                            Line(num, num2, 32f, 17f, 45f, 22f) ||
                            Line(num, num2, 45f, 22f, 45f, 31f) ||
                            Line(num, num2, 32f, 17f, 32f, 9f) ||
                            Line(num, num2, 23f, 9f, 41f, 9f);
                        break;

                    case "speaker":
                        visible =
                            Line(num, num2, 12f, 23f, 23f, 23f) ||
                            Line(num, num2, 23f, 23f, 36f, 12f) ||
                            Line(num, num2, 36f, 12f, 36f, 52f) ||
                            Line(num, num2, 36f, 52f, 23f, 41f) ||
                            Line(num, num2, 23f, 41f, 12f, 41f) ||
                            Line(num, num2, 12f, 41f, 12f, 23f) ||
                            Line(num, num2, 43f, 23f, 49f, 32f) ||
                            Line(num, num2, 49f, 32f, 43f, 41f) ||
                            Line(num, num2, 48f, 15f, 58f, 32f) ||
                            Line(num, num2, 58f, 32f, 48f, 49f);
                        break;

                    case "phone":
                        visible =
                            Line(num, num2, 18f, 48f, 14f, 39f, 5f) ||
                            Line(num, num2, 14f, 39f, 24f, 24f, 5f) ||
                            Line(num, num2, 24f, 24f, 39f, 14f, 5f) ||
                            Line(num, num2, 39f, 14f, 48f, 18f, 5f) ||
                            Line(num, num2, 18f, 48f, 25f, 42f, 5f) ||
                            Line(num, num2, 48f, 18f, 42f, 25f, 5f);
                        break;

                    case "bank":
                        visible =
                            Line(num, num2, 10f, 41f, 32f, 54f) ||
                            Line(num, num2, 32f, 54f, 54f, 41f) ||
                            Line(num, num2, 12f, 39f, 52f, 39f) ||
                            Line(num, num2, 12f, 12f, 52f, 12f) ||
                            Line(num, num2, 18f, 18f, 18f, 33f) ||
                            Line(num, num2, 32f, 18f, 32f, 33f) ||
                            Line(num, num2, 46f, 18f, 46f, 33f);
                        break;

                    case "nearby":
                        visible =
                            distance < 5f ||
                            (Math.Abs(distance - 15f) < 2.5f && num2 > 24f) ||
                            (Math.Abs(distance - 26f) < 2.5f && num2 > 24f) ||
                            Line(num, num2, 23f, 10f, 32f, 21f) ||
                            Line(num, num2, 32f, 21f, 41f, 10f);
                        break;

                    case "settings":
                        visible =
                            Math.Abs(distance - 13f) < 3f ||
                            (distance > 22f && distance < 28f) ||
                            (distance > 25f && distance < 32f &&
                            Math.Abs(Math.Sin(
                                Math.Atan2(num2 - 32f, num - 32f) * 4.0)) > 0.7);
                        break;
                }

                texture.SetPixel(
                    x,
                    y,
                    new Color(1f, 1f, 1f, visible ? 1f : 0f));
            }
        }

        texture.Apply(false, false);

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, 64f, 64f),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(0f, 0f, 0f, 0f));
    }
}
