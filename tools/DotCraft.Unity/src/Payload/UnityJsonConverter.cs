using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DotCraft.Unity
{
    sealed class UnityJsonConverter : JsonConverter
    {
        public override bool CanRead { get { return false; } }

        public override bool CanConvert(Type type)
        {
            return type == typeof(Vector2) || type == typeof(Vector2Int)
                || type == typeof(Vector3) || type == typeof(Vector3Int) || type == typeof(Vector4)
                || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Color32)
                || type == typeof(Rect) || type == typeof(RectInt)
                || type == typeof(Bounds) || type == typeof(BoundsInt)
                || type == typeof(Matrix4x4) || type == typeof(Ray) || type == typeof(Ray2D)
                || type == typeof(Plane) || typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Vector2 vector2) WriteVector(writer, vector2.x, vector2.y);
            else if (value is Vector2Int vector2Int) WriteVector(writer, vector2Int.x, vector2Int.y);
            else if (value is Vector3 vector3) WriteVector(writer, vector3.x, vector3.y, vector3.z);
            else if (value is Vector3Int vector3Int) WriteVector(writer, vector3Int.x, vector3Int.y, vector3Int.z);
            else if (value is Vector4 vector4) WriteVector(writer, vector4.x, vector4.y, vector4.z, vector4.w);
            else if (value is Quaternion quaternion) WriteVector(writer, quaternion.x, quaternion.y, quaternion.z, quaternion.w);
            else if (value is Color color) WriteColor(writer, color.r, color.g, color.b, color.a);
            else if (value is Color32 color32) WriteColor(writer, color32.r, color32.g, color32.b, color32.a);
            else if (value is Rect rect) WriteRect(writer, rect.x, rect.y, rect.width, rect.height);
            else if (value is RectInt rectInt) WriteRect(writer, rectInt.x, rectInt.y, rectInt.width, rectInt.height);
            else if (value is Bounds bounds) WriteBounds(writer, serializer, "center", bounds.center, bounds.size);
            else if (value is BoundsInt boundsInt) WriteBounds(writer, serializer, "position", boundsInt.position, boundsInt.size);
            else if (value is Matrix4x4 matrix) WriteMatrix(writer, matrix);
            else if (value is Ray ray) WriteRay(writer, serializer, ray.origin, ray.direction);
            else if (value is Ray2D ray2D) WriteRay(writer, serializer, ray2D.origin, ray2D.direction);
            else if (value is Plane plane) WritePlane(writer, serializer, plane);
            else WriteObject(writer, (UnityEngine.Object)value);
        }

        public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        static void WriteVector(JsonWriter writer, params object[] values)
        {
            string[] names = { "x", "y", "z", "w" };
            writer.WriteStartObject();
            for (var index = 0; index < values.Length; index++) Write(writer, names[index], values[index]);
            writer.WriteEndObject();
        }

        static void WriteColor(JsonWriter writer, object red, object green, object blue, object alpha)
        {
            writer.WriteStartObject();
            Write(writer, "r", red); Write(writer, "g", green); Write(writer, "b", blue); Write(writer, "a", alpha);
            writer.WriteEndObject();
        }

        static void WriteRect(JsonWriter writer, object x, object y, object width, object height)
        {
            writer.WriteStartObject();
            Write(writer, "x", x); Write(writer, "y", y); Write(writer, "width", width); Write(writer, "height", height);
            writer.WriteEndObject();
        }

        static void WriteBounds(JsonWriter writer, JsonSerializer serializer, string positionName, object position, object size)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(positionName); serializer.Serialize(writer, position);
            writer.WritePropertyName("size"); serializer.Serialize(writer, size);
            writer.WriteEndObject();
        }

        static void WriteMatrix(JsonWriter writer, Matrix4x4 value)
        {
            writer.WriteStartObject();
            Write(writer, "m00", value.m00); Write(writer, "m01", value.m01); Write(writer, "m02", value.m02); Write(writer, "m03", value.m03);
            Write(writer, "m10", value.m10); Write(writer, "m11", value.m11); Write(writer, "m12", value.m12); Write(writer, "m13", value.m13);
            Write(writer, "m20", value.m20); Write(writer, "m21", value.m21); Write(writer, "m22", value.m22); Write(writer, "m23", value.m23);
            Write(writer, "m30", value.m30); Write(writer, "m31", value.m31); Write(writer, "m32", value.m32); Write(writer, "m33", value.m33);
            writer.WriteEndObject();
        }

        static void WriteRay(JsonWriter writer, JsonSerializer serializer, object origin, object direction)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("origin"); serializer.Serialize(writer, origin);
            writer.WritePropertyName("direction"); serializer.Serialize(writer, direction);
            writer.WriteEndObject();
        }

        static void WritePlane(JsonWriter writer, JsonSerializer serializer, Plane value)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("normal"); serializer.Serialize(writer, value.normal);
            Write(writer, "distance", value.distance);
            writer.WriteEndObject();
        }

        static void WriteObject(JsonWriter writer, UnityEngine.Object value)
        {
            writer.WriteStartObject();
            Write(writer, "type", value.GetType().FullName);
            Write(writer, "name", value.name);
            Write(writer, "instanceId", value.GetInstanceID());
            writer.WriteEndObject();
        }

        static void Write(JsonWriter writer, string name, object value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }
    }
}
