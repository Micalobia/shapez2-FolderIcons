using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Micalobia.Shapez2.FolderIcons;

public static class HookHelper
{
    private const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags ALL_DECLARED = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string IL_DUMP_DIR = @"mod logs\folder-icons";

    // ReSharper disable once UnusedTypeParameter
    // The generic type is used in `ResolveParameterType`, this type represents `ref` and `out` arguments in our helpers.
    public sealed class Ref<T>;

    // I understand that SharpDetour has a helper for getting methods. I like these ones because they look nicer, at the cost of more relaxed compile time checking.
    // On methods with overloads, or methods you expect the signature to change, use SharpDetour instead.
    public static MethodInfo GetMethod<T>(string name) => GetMethod(typeof(T), name);

    public static MethodInfo GetMethod<T>(string name, params Type[] parameters) => GetMethod(typeof(T), name, parameters);

    public static MethodInfo GetMethod<T, TArg0>(string name) =>
        GetMethod<T>(name, typeof(TArg0));

    public static MethodInfo GetMethod<T, TArg0, TArg1>(string name) =>
        GetMethod<T>(name, typeof(TArg0), typeof(TArg1));

    public static MethodInfo GetMethod<T, TArg0, TArg1, TArg2>(string name) =>
        GetMethod<T>(name, typeof(TArg0), typeof(TArg1), typeof(TArg2));

    public static MethodInfo GetMethod<T, TArg0, TArg1, TArg2, TArg3>(string name) =>
        GetMethod<T>(name, typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3));

    public static MethodInfo GetMethod<T, TArg0, TArg1, TArg2, TArg3, TArg4>(string name) =>
        GetMethod<T>(name, typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4));

    public static MethodInfo GetMethod<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(string name) =>
        GetMethod<T>(name, typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5));

    public static ConstructorInfo GetConstructor<T>() => typeof(T).GetConstructor();

    public static ConstructorInfo GetConstructor<T>(params Type[] parameters) => GetConstructor(typeof(T), parameters);

    public static ConstructorInfo GetConstructor<T, TArg0>() =>
        GetConstructor<T>(typeof(TArg0));

    public static ConstructorInfo GetConstructor<T, TArg0, TArg1>() =>
        GetConstructor<T>(typeof(TArg0), typeof(TArg1));

    public static ConstructorInfo GetConstructor<T, TArg0, TArg1, TArg2>() =>
        GetConstructor<T>(typeof(TArg0), typeof(TArg1), typeof(TArg2));

    public static ConstructorInfo GetConstructor<T, TArg0, TArg1, TArg2, TArg3>() =>
        GetConstructor<T>(typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3));

    public static ConstructorInfo GetConstructor<T, TArg0, TArg1, TArg2, TArg3, TArg4>() =>
        GetConstructor<T>(typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4));

    public static ConstructorInfo GetConstructor<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>() =>
        GetConstructor<T>(typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3), typeof(TArg4), typeof(TArg5));

    public static MethodInfo GetGetter<T>(string name) => typeof(T).GetProperty(name, ALL_DECLARED)?.GetGetMethod(true);

    public static MethodInfo GetSetter<T>(string name) => typeof(T).GetProperty(name, ALL_DECLARED)?.GetSetMethod(true);

    public static FieldInfo GetField<T>(string name) => GetField(typeof(T), name);

    public static ILHook CreateILHook<T>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T>(string name, ILContext.Manipulator manipulator, params Type[] parameters) =>
        GetMethod<T>(name, parameters).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0, TArg1>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0, TArg1>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0, TArg1, TArg2>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2, TArg3>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0, TArg1, TArg2, TArg3>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2, TArg3, TArg4>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0, TArg1, TArg2, TArg3, TArg4>(name).CreateILHook(manipulator);

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(string name, ILContext.Manipulator manipulator) =>
        GetMethod<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(name).CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T>(ILContext.Manipulator manipulator) =>
        GetConstructor<T>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T>(ILContext.Manipulator manipulator, params Type[] parameters) =>
        GetConstructor<T>(parameters).CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0, TArg1>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0, TArg1>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0, TArg1, TArg2>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0, TArg1, TArg2>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0, TArg1, TArg2, TArg3>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0, TArg1, TArg2, TArg3>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0, TArg1, TArg2, TArg3, TArg4>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0, TArg1, TArg2, TArg3, TArg4>().CreateILHook(manipulator);

    public static ILHook CreateConstructorILHook<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(ILContext.Manipulator manipulator) =>
        GetConstructor<T, TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>().CreateILHook(manipulator);

    public static MethodBase GetRuntimeBase(LambdaExpression original) =>
        StripConvert(original.Body) is NewExpression ? GetRuntimeConstructor(original) : GetRuntimeMethod(original);

    public static MethodBase GetRuntimeBase(Expression<Action> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0>(Expression<Action<TArg0>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1>(Expression<Action<TArg0, TArg1>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2>(Expression<Action<TArg0, TArg1, TArg2>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3>(Expression<Action<TArg0, TArg1, TArg2, TArg3>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(
        Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>(
        Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TResult>(Expression<Func<TResult>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TResult>(Expression<Func<TArg0, TResult>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TResult>(Expression<Func<TArg0, TArg1, TResult>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TResult>(Expression<Func<TArg0, TArg1, TArg2, TResult>> original) => GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TResult>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodBase GetRuntimeBase<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>> original) =>
        GetRuntimeBase((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod(LambdaExpression original)
    {
        var method = GetMethodCallExpression(original).Method;
        return GetRuntimeMethod(method.DeclaringType, method);
    }

    public static MethodInfo GetRuntimeMethod(Expression<Action> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0>(Expression<Action<TArg0>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1>(Expression<Action<TArg0, TArg1>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2>(Expression<Action<TArg0, TArg1, TArg2>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3>(Expression<Action<TArg0, TArg1, TArg2, TArg3>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(
        Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>(
        Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>(
        Expression<Action<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7, TArg8, TArg9>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TResult>(Expression<Func<TResult>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TResult>(Expression<Func<TArg0, TResult>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TResult>(Expression<Func<TArg0, TArg1, TResult>> original) => GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TResult>(Expression<Func<TArg0, TArg1, TArg2, TResult>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TResult>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static MethodInfo GetRuntimeMethod<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>> original) =>
        GetRuntimeMethod((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor(LambdaExpression original)
    {
        var constructor = GetNewExpression(original).Constructor;
        return constructor == null
            ? throw new InvalidOperationException("Expression does not reference a runtime constructor.")
            : GetRuntimeConstructor(constructor.DeclaringType, constructor);
    }

    public static ConstructorInfo GetRuntimeConstructor<TResult>(Expression<Func<TResult>> original) => GetRuntimeConstructor((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor<TArg0, TResult>(Expression<Func<TArg0, TResult>> original) => GetRuntimeConstructor((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor<TArg0, TArg1, TResult>(Expression<Func<TArg0, TArg1, TResult>> original) =>
        GetRuntimeConstructor((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor<TArg0, TArg1, TArg2, TResult>(Expression<Func<TArg0, TArg1, TArg2, TResult>> original) =>
        GetRuntimeConstructor((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor<TArg0, TArg1, TArg2, TArg3, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TResult>> original) =>
        GetRuntimeConstructor((LambdaExpression)original);

    public static ConstructorInfo GetRuntimeConstructor<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>(Expression<Func<TArg0, TArg1, TArg2, TArg3, TArg4, TResult>> original) =>
        GetRuntimeConstructor((LambdaExpression)original);

    extension(MethodBase self)
    {
        public ILHook CreateILHook(ILContext.Manipulator manipulator) => new(self, manipulator);
    }

    extension(Type self)
    {
        public MethodInfo GetMethod(string name, params Type[] parameters) => parameters is { Length: > 0 }
            ? self.GetMethod(name, ALL_DECLARED, null, ResolveParameterTypes(parameters), null)
            : self.GetMethod(name, ALL_DECLARED);

        public ConstructorInfo GetConstructor(params Type[] parameters)
            => self.GetConstructor(ALL_DECLARED, null, ResolveParameterTypes(parameters), null);

        public MethodInfo GetGetter(string name) => self.GetProperty(name, ALL_DECLARED)?.GetGetMethod(true);

        public MethodInfo GetSetter(string name) => self.GetProperty(name, ALL_DECLARED)?.GetSetMethod(true);

        public FieldInfo GetField(string name) => self.GetField(name, ALL_DECLARED);
    }

    extension(Instruction self)
    {
        public bool MatchTrue() => self.MatchLdcI4(1);

        public bool MatchFalse() => self.MatchLdcI4(0);

        public bool MatchLdloc<T>(ILContext ctx, out VariableDefinition variable)
        {
            variable = null;
            if (!self.MatchLdloc(out var index))
                return false;

            variable = ctx.Body.Variables[index];
            return MatchesVariableType<T>(variable);
        }

        public bool MatchLdloca<T>(ILContext ctx, out VariableDefinition variable)
        {
            variable = null;
            if (!self.MatchLdloca(out var index))
                return false;

            variable = ctx.Body.Variables[index];
            return MatchesVariableType<T>(variable);
        }

        public bool MatchStloc<T>(ILContext ctx, out VariableDefinition variable)
        {
            variable = null;
            if (!self.MatchStloc(out var index))
                return false;

            variable = ctx.Body.Variables[index];
            return MatchesVariableType<T>(variable);
        }

        public bool MatchGetter<T>(string propertyName) => self.MatchGetter(typeof(T), propertyName);

        public bool MatchGetter(Type type, string propertyName) =>
            self.MatchCall(type, GetGetterName(propertyName)) ||
            self.MatchCallvirt(type, GetGetterName(propertyName));

        public bool MatchSetter<T>(string propertyName) => self.MatchSetter(typeof(T), propertyName);

        public bool MatchSetter(Type type, string propertyName) =>
            self.MatchCall(type, GetSetterName(propertyName)) ||
            self.MatchCallvirt(type, GetSetterName(propertyName));
    }

    extension(ILContext self)
    {
        public int ReplaceStrings(string replace, string with, int count = -1, int start = 0)
        {
            count = count < 0 ? int.MaxValue : count;
            var replacements = 0;

            // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
            foreach (var instruction in self.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand as string != replace)
                    continue;
                if (start-- > 0)
                    continue;
                if (count-- <= 0)
                    break;
                instruction.Operand = with;
                ++replacements;
            }

            return replacements;
        }

        public void DumpIL(string methodName)
        {
            var dumpDirectory = GetILDumpDirectory();
            Directory.CreateDirectory(dumpDirectory);
            var filename = methodName.Replace(':', '_') + ".il.txt";
            File.WriteAllText(Path.Combine(dumpDirectory, filename), self.ToString());
        }
    }

    extension(ILCursor self)
    {
        public void EmitFalse() => self.Emit(OpCodes.Ldc_I4_0);

        public void EmitTrue() => self.Emit(OpCodes.Ldc_I4_1);

        /// <summary>
        /// Removes from the cursor through the last matched instruction. Allows gaps
        /// </summary>
        public bool TryRemoveThroughNext(params Func<Instruction, bool>[] predicates)
        {
            if (!self.TryFindNext(out var cursors, predicates))
                return false;
            self.RemoveRange(cursors[^1].Index + 1 - self.Index);
            return true;
        }

        /// <summary>
        /// Removes from the cursor through the last matched instruction. Allows gaps
        /// </summary>
        public ILCursor RemoveThroughNext(params Func<Instruction, bool>[] predicates)
        {
            self.FindNext(out var cursors, predicates);
            self.RemoveRange(cursors[^1].Index + 1 - self.Index);
            return self;
        }
    }

    private static MethodInfo GetRuntimeMethod(Type type, MethodInfo method)
    {
        try
        {
            var runtimeMethod = type.GetMethod(method.Name, ALL);
            return runtimeMethod == null ? throw new MissingMethodException(type.FullName, method.Name) : runtimeMethod;
        }
        catch (AmbiguousMatchException)
        {
            foreach (var candidate in type.GetMethods(ALL).Where(candidate => candidate.Name == method.Name))
                if (ParameterTypesMatch(method, candidate))
                    return candidate;
        }

        throw new MissingMethodException(type.FullName, method.Name);
    }

    private static ConstructorInfo GetRuntimeConstructor(Type type, ConstructorInfo constructor)
    {
        foreach (var candidate in type.GetConstructors(ALL))
            if (ParameterTypesMatch(constructor, candidate))
                return candidate;

        throw new MissingMethodException(type.FullName, ".ctor");
    }

    private static bool ParameterTypesMatch(MethodBase left, MethodBase right)
    {
        var leftParameters = left.GetParameters();
        var rightParameters = right.GetParameters();
        if (leftParameters.Length != rightParameters.Length)
            return false;

        return !leftParameters.Where((t, i) => t.ParameterType != rightParameters[i].ParameterType).Any();
    }

    private static MethodCallExpression GetMethodCallExpression(LambdaExpression expression)
    {
        if (StripConvert(expression.Body) is MethodCallExpression methodCall)
            return methodCall;

        throw new ArgumentException("Expression body must be a method call.", nameof(expression));
    }

    private static NewExpression GetNewExpression(LambdaExpression expression)
    {
        if (StripConvert(expression.Body) is NewExpression newExpression)
            return newExpression;

        throw new ArgumentException("Expression body must be a constructor call.", nameof(expression));
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;

        return expression;
    }

    private static Type[] ResolveParameterTypes(Type[] parameters)
    {
        var resolved = new Type[parameters.Length];
        for (var i = 0; i < parameters.Length; ++i)
            resolved[i] = ResolveParameterType(parameters[i]);

        return resolved;
    }

    private static Type ResolveParameterType(Type parameter)
    {
        if (parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(Ref<>))
            return parameter.GetGenericArguments()[0].MakeByRefType();

        return parameter;
    }

    private static bool MatchesVariableType<T>(VariableDefinition variable) => variable.VariableType.FullName == GetCecilFullName(typeof(T));

    private static string GetCecilFullName(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName;

        var genericDefinition = type.GetGenericTypeDefinition();
        var genericArguments = string.Join(",", type.GetGenericArguments().Select(GetCecilFullName));
        return genericDefinition.FullName + "<" + genericArguments + ">";
    }

    private static string GetILDumpDirectory()
    {
        var persistentPath = Environment.GetEnvironmentVariable("SPZ2_PERSISTENT");
        return Path.Combine(
            string.IsNullOrWhiteSpace(persistentPath) ? Path.GetTempPath() : persistentPath,
            IL_DUMP_DIR
        );
    }

    private static string GetGetterName(string propertyName) => propertyName.StartsWith("get_", StringComparison.Ordinal)
        ? propertyName
        : "get_" + propertyName;

    private static string GetSetterName(string propertyName) => propertyName.StartsWith("set_", StringComparison.Ordinal)
        ? propertyName
        : "set_" + propertyName;
}
