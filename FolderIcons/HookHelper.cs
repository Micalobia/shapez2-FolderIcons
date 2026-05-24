using System;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace FolderIcons;

public static class HookHelper
{
    private const BindingFlags ALL_DECLARED = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static MethodInfo GetMethod<T>(string name, params Type[] parameters) => GetMethod(typeof(T), name, parameters);

    public static MethodInfo GetMethod(Type type, string name, params Type[] parameters) =>
        parameters is { Length: > 0 }
            ? type.GetMethod(name, ALL_DECLARED, null, parameters, null)
            : type.GetMethod(name, ALL_DECLARED);

    public static ILHook CreateILHook<T>(string name, ILContext.Manipulator manipulator, params Type[] parameters) => new(GetMethod<T>(name, parameters), manipulator);

    public static ILHook CreateILHook<T, TArg0>(string name, ILContext.Manipulator manipulator) =>
        CreateILHook<T>(name, manipulator, typeof(TArg0));

    public static ILHook CreateILHook<T, TArg0, TArg1>(string name, ILContext.Manipulator manipulator) =>
        CreateILHook<T>(name, manipulator, typeof(TArg0), typeof(TArg1));

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2>(string name, ILContext.Manipulator manipulator) =>
        CreateILHook<T>(name, manipulator, typeof(TArg0), typeof(TArg1), typeof(TArg2));

    public static ILHook CreateILHook<T, TArg0, TArg1, TArg2, TArg3>(string name, ILContext.Manipulator manipulator) =>
        CreateILHook<T>(name, manipulator, typeof(TArg0), typeof(TArg1), typeof(TArg2), typeof(TArg3));

    public static FieldInfo GetField<T>(string name) => GetField(typeof(T), name);

    public static FieldInfo GetField(Type type, string name) => type.GetField(name, ALL_DECLARED);
}