using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;

namespace RevitUITesting.Tests.Unit
{
    /// <summary>
    /// Публичный API (снимок RevitUITesting.PublicApi.approved.txt) не должен пропадать и менять
    /// сигнатуры: им пользуются UI-тесты плагинов. Добавления разрешены.
    /// Снимок только растёт: после добавлений обновить его — REVIT_UI_UPDATE_API=1.
    /// </summary>
    [TestFixture]
    public class PublicApiTests
    {
        private const string SnapshotFile = "RevitUITesting.PublicApi.approved.txt";

        [Test]
        public void Published_api_is_preserved()
        {
            List<string> current = Describe(typeof(RevitUiConfig).Assembly);
            if (Environment.GetEnvironmentVariable("REVIT_UI_UPDATE_API") == "1")
            {
                File.WriteAllLines(Path.Combine(SourceDirectory(), SnapshotFile), current, new UTF8Encoding(false));
                Assert.Inconclusive("Снимок API перезаписан: " + current.Count + " строк.");
            }
            string[] approved = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotFile));
            HashSet<string> set = new HashSet<string>(current, StringComparer.Ordinal);
            string[] missing = approved.Where(line => line.Length > 0 && !set.Contains(line)).ToArray();
            Assert.That(missing, Is.Empty, "Из публичного API пропали или изменились члены:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }

        private static string SourceDirectory([CallerFilePath] string path = null)
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(path));
        }

        internal static List<string> Describe(Assembly assembly)
        {
            List<string> lines = new List<string>();
            foreach (Type type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                string typeName = TypeName(type);
                lines.Add(typeName + " | " + TypeKind(type));
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                if (type.IsEnum)
                {
                    foreach (string name in Enum.GetNames(type))
                    {
                        lines.Add(typeName + " | enum " + name + " = " + Convert.ToInt64(Enum.Parse(type, name)));
                    }
                    continue;
                }
                bool canInherit = !type.IsSealed;
                foreach (ConstructorInfo ctor in type.GetConstructors(flags).Where(c => Visible(c, canInherit)))
                {
                    lines.Add(typeName + " | " + Access(ctor) + "ctor(" + Parameters(ctor.GetParameters()) + ")");
                }
                foreach (MethodInfo method in type.GetMethods(flags).Where(m => !m.IsSpecialName && Visible(m, canInherit)))
                {
                    string generic = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : "";
                    lines.Add(typeName + " | " + Access(method) + (method.IsStatic ? "static " : "") + "method " + method.Name + generic
                        + "(" + Parameters(method.GetParameters()) + ") : " + TypeName(method.ReturnType));
                }
                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    MethodInfo getter = property.GetGetMethod(true);
                    MethodInfo setter = property.GetSetMethod(true);
                    bool get = getter != null && Visible(getter, canInherit);
                    bool set = setter != null && Visible(setter, canInherit);
                    if (!get && !set)
                    {
                        continue;
                    }
                    MethodInfo any = get ? getter : setter;
                    string index = property.GetIndexParameters().Length > 0 ? "[" + Parameters(property.GetIndexParameters()) + "]" : "";
                    lines.Add(typeName + " | " + Access(any) + (any.IsStatic ? "static " : "") + "property " + property.Name + index + " : " + TypeName(property.PropertyType)
                        + " {" + (get ? " get;" : "") + (set ? " set;" : "") + " }");
                }
                foreach (FieldInfo field in type.GetFields(flags).Where(f => !f.IsSpecialName && (f.IsPublic || (canInherit && (f.IsFamily || f.IsFamilyOrAssembly)))))
                {
                    string value = field.IsLiteral ? " = " + Literal(field.GetRawConstantValue()) : "";
                    lines.Add(typeName + " | " + (field.IsPublic ? "" : "protected ") + (field.IsLiteral ? "const " : field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : "")
                        + "field " + field.Name + " : " + TypeName(field.FieldType) + value);
                }
                foreach (EventInfo evt in type.GetEvents(flags).Where(e => Visible(e.GetAddMethod(true), canInherit)))
                {
                    lines.Add(typeName + " | event " + evt.Name + " : " + TypeName(evt.EventHandlerType));
                }
            }
            return lines;
        }

        private static bool Visible(MethodBase method, bool canInherit)
        {
            return method != null && (method.IsPublic || (canInherit && (method.IsFamily || method.IsFamilyOrAssembly)));
        }

        private static string Access(MethodBase method)
        {
            return method.IsPublic ? "" : "protected ";
        }

        private static string TypeKind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }
            if (type.IsInterface)
            {
                return "interface";
            }
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                return "delegate";
            }
            if (type.IsValueType)
            {
                return "struct";
            }
            string kind = type.IsAbstract && type.IsSealed ? "static class" : "class";
            return type.BaseType != null && type.BaseType != typeof(object) ? kind + " : " + TypeName(type.BaseType) : kind;
        }

        private static string Parameters(ParameterInfo[] parameters)
        {
            return string.Join(", ", parameters.Select(p =>
            {
                string prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : p.IsDefined(typeof(ParamArrayAttribute), false) ? "params " : "";
                Type type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                string def = p.HasDefaultValue ? " = " + Literal(p.DefaultValue) : "";
                return prefix + TypeName(type) + " " + p.Name + def;
            }));
        }

        private static string Literal(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is string s)
            {
                return "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
            }
            if (value is bool b)
            {
                return b ? "true" : "false";
            }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string TypeName(Type type)
        {
            if (type.IsArray)
            {
                return TypeName(type.GetElementType()) + "[]";
            }
            if (type.IsGenericParameter)
            {
                return type.Name;
            }
            if (Nullable.GetUnderlyingType(type) is Type underlying)
            {
                return TypeName(underlying) + "?";
            }
            string name = type.IsNested ? TypeName(type.DeclaringType) + "+" + type.Name : type.Namespace + "." + type.Name;
            if (type.IsGenericType)
            {
                int tick = name.IndexOf('`');
                name = (tick > 0 ? name.Substring(0, tick) : name) + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
            }
            return name;
        }
    }
}
