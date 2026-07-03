using System;
using JetBrains.Annotations;

namespace Micalobia.Shapez2.FolderIcons;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
[MeansImplicitUse]
internal sealed class LoggerFieldAttribute : Attribute;
