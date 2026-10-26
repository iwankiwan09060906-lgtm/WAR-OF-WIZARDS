// Unity 없이 전투 코드를 돌리기 위한 아주 작은 대역 (전투 코드가 실제로 쓰는 것만).
// 전투 판정 코드는 UnityClient/Assets/_Project 의 진짜 파일을 그대로 컴파일한다 (BotLab.csproj).
using System;

namespace Fusion
{
    public interface INetworkStruct { }
}

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : zero; } }
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector2 MoveTowards(Vector2 cur, Vector2 target, float maxDelta)
        {
            Vector2 d = target - cur;
            float m = d.magnitude;
            return m <= maxDelta || m < 1e-6f ? target : cur + d / m * maxDelta;
        }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => $"({x:0.00}, {y:0.00})";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1f };
        public static Quaternion LookRotation(Vector3 forward) => identity;
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => identity;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f);
        public static Color red => new Color(1f, 0f, 0f);
        public static Color green => new Color(0f, 1f, 0f);
        public static Color blue => new Color(0f, 0f, 1f);
        public static Color yellow => new Color(1f, 0.92f, 0.016f);
        public static Color cyan => new Color(0f, 1f, 1f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Epsilon = 1.401298E-45f;
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float MoveTowards(float cur, float target, float maxDelta) => Math.Abs(target - cur) <= maxDelta ? target : cur + Math.Sign(target - cur) * maxDelta;
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
    }

    public static class Debug
    {
        /// <summary>BotLab 은 기본으로 로그를 끈다 (수백 판 · 수만 줄)</summary>
        public static bool Enabled;
        public static void Log(object m) { if (Enabled) Console.WriteLine(m); }
        public static void LogWarning(object m) { if (Enabled) Console.WriteLine("[W] " + m); }
        public static void LogError(object m) { Console.WriteLine("[E] " + m); }
        public static void LogException(Exception e) { Console.WriteLine("[EXC] " + e); }
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static float unscaledTime => time;
        public static float realtimeSinceStartup => time;
    }

    public static class Random
    {
        private static readonly System.Random R = new System.Random(1);
        public static float value => (float)R.NextDouble();
        public static float Range(float min, float max) => min + (max - min) * value;
        public static int Range(int min, int max) => R.Next(min, max);
    }

    public class Object
    {
        public string name { get; set; } = "";
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.All)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float min) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SpaceAttribute : Attribute { public SpaceAttribute(float h = 8f) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string menuName { get; set; } public string fileName { get; set; } public int order { get; set; } }
}

namespace UnityEngine.InputSystem
{
    public enum Key { None, Space, Q, W, E, LeftArrow, RightArrow, UpArrow, DownArrow }
}

namespace SpellboundVR.Utils
{
    /// <summary>BotLab: 키보드 없음 (봇 수동 조종 꺼짐)</summary>
    public static class KeyInput
    {
        public static bool Down(UnityEngine.InputSystem.Key key) => false;
        public static bool AltHeld() => false;
        public static int SlotKeyDown() => -1;
    }
}
