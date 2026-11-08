using System;
using System.Collections.Generic;
using System.Reflection;

namespace OverdriveTests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertionException(string message) : Exception(message) { }

    public static class Assert
    {
        public static void True(bool condition, string message = "expected true")
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void False(bool condition, string message = "expected false") => True(!condition, message);

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionException($"{message ?? "values differ"}: expected <{expected}>, actual <{actual}>");
        }

        public static void Near(double expected, double actual, double tol = 1e-9, string message = null)
        {
            if (!(Math.Abs(expected - actual) <= tol))
                throw new AssertionException($"{message ?? "values differ"}: expected <{expected}>, actual <{actual}> (tol {tol})");
        }

        public static void Throws<TEx>(Action action) where TEx : Exception
        {
            try { action(); }
            catch (TEx) { return; }
            catch (Exception e) { throw new AssertionException($"expected {typeof(TEx).Name}, got {e.GetType().Name}"); }
            throw new AssertionException($"expected {typeof(TEx).Name}, nothing thrown");
        }
    }

    public static class Program
    {
        public static int Main()
        {
            int passed = 0, failed = 0;
            foreach (var type in typeof(Program).Assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.GetCustomAttribute<TestAttribute>() == null) continue;
                    string name = $"{type.Name}.{method.Name}";
                    try
                    {
                        method.Invoke(null, null);
                        ++passed;
                        Console.WriteLine($"PASS {name}");
                    }
                    catch (Exception e)
                    {
                        ++failed;
                        var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                        Console.WriteLine($"FAIL {name}: {inner.Message}");
                    }
                }
            }
            Console.WriteLine($"{passed} passed, {failed} failed");
            return failed > 0 ? 1 : 0;
        }
    }
}
