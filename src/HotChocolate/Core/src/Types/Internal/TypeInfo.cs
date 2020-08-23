﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HotChocolate.Types;
using HotChocolate.Utilities;
using Nullable = HotChocolate.Utilities.Nullable;

#nullable enable

namespace HotChocolate.Internal
{
    public sealed partial class TypeInfo2
        : ITypeFactory
        , ITypeInfo
    {
        private readonly IExtendedType _extendedType;

        private TypeInfo2(
            Type namedType,
            Type originalType,
            IReadOnlyList<TypeComponentKind> components,
            bool isSchemaType,
            IExtendedType extendedType)
        {
            NamedType = namedType;
            OriginalType = originalType;
            Components = components;
            IsSchemaType = isSchemaType;
            IsRuntimeType = !isSchemaType;
            _extendedType = extendedType;
        }

        /// <summary>
        /// Gets the type component that represents the named type.
        /// </summary>
        public Type NamedType { get; }

        /// <summary>
        /// Gets the original type from which this type info was inferred.
        /// </summary>
        public Type OriginalType { get; }

        /// <summary>
        /// The components represent the GraphQL type structure.
        /// </summary>
        public IReadOnlyList<TypeComponentKind> Components { get; }

        /// <summary>
        /// Defines if the <see cref="NamedType"/> is a GraphQL schema type.
        /// </summary>
        public bool IsSchemaType { get; }

        /// <summary>
        /// Defines if the <see cref="NamedType"/> is a runtime type.
        /// </summary>
        public bool IsRuntimeType { get; }

        /// <summary>
        /// Gets the extended type that contains information
        /// about type arguments and nullability.
        /// </summary>
        public IExtendedType GetExtendedType() => _extendedType;

        /// <summary>
        /// Creates a type structure with the <paramref name="namedType"/>.
        /// </summary>
        /// <param name="namedType">The named type component.</param>
        /// <returns>
        /// Returns a GraphQL type structure.
        /// </returns>
        public IType CreateType(INamedType namedType)
        {
            if (Components.Count == 1)
            {
                return namedType;
            }

            IType current = namedType;

            for (var i = Components.Count - 2; i >= 0; i--)
            {
                switch (Components[i])
                {
                    case TypeComponentKind.Named:
                        throw new InvalidOperationException();

                    case TypeComponentKind.NonNull:
                        current = new NonNullType(current);
                        break;

                    case TypeComponentKind.List:
                        current = new ListType(current);
                        break;
                }
            }

            return current;
        }

        public static TypeInfo2 Create(Type type, bool[]? nullable = null)
        {
            ExtendedType extendedType = ExtendedType.FromType(type);

            if (nullable is not null)
            {
                var nullableState = new Nullable[nullable.Length];
                for (var i = 0; i < nullable.Length; i++)
                {
                    nullableState[i] = nullable[i] ? Nullable.Yes : Nullable.No;
                }

                return Create(ExtendedTypeRewriter.Rewrite(extendedType, nullableState));
            }

            return Create(extendedType, type);
        }

        public static TypeInfo2 Create(MemberInfo member)
        {
            switch (member)
            {
                case PropertyInfo p:
                    return Create(
                        ExtendedType.FromExtendedType(
                            NullableHelper.GetReturnType(member)),
                        p.PropertyType);

                case MethodInfo m:
                    return Create(
                        ExtendedType.FromExtendedType(
                            NullableHelper.GetReturnType(member)),
                        m.ReturnType);

                default:
                    throw new NotSupportedException(
                        "Only PropertyInfo and MethodInfo are supported.");
            }
        }

        public static TypeInfo2 Create(IExtendedType type) =>
            Create(type, null);

        private static TypeInfo2 Create(IExtendedType type, Type? originalType)
        {
            if (TryCreate(type, originalType, out TypeInfo2? typeInfo))
            {
                return typeInfo;
            }

            throw new NotSupportedException(
                "The provided type structure is not supported.");
        }

        public static bool TryCreate(
            Type type,
            [NotNullWhen(true)] out TypeInfo2? typeInfo) =>
            TryCreate(type, null, out typeInfo);

        public static bool TryCreate(
            Type type,
            bool[]? nullable,
            [NotNullWhen(true)] out TypeInfo2? typeInfo)
        {
            IExtendedType extendedType = ExtendedType.FromType(type);

            if (nullable is not null)
            {
                var nullableState = new Nullable[nullable.Length];
                for (var i = 0; i < nullable.Length; i++)
                {
                    nullableState[i] = nullable[i] ? Nullable.Yes : Nullable.No;
                }

                extendedType = ExtendedType.FromType(
                    ExtendedTypeRewriter.Rewrite(extendedType, nullableState));
            }

            return TryCreate(extendedType, type, out typeInfo);
        }

        public static bool TryCreate(
            MemberInfo member,
            [NotNullWhen(true)] out TypeInfo2? typeInfo)
        {
            switch (member)
            {
                case PropertyInfo p:
                    return TryCreate(
                        new NullableHelper().GetPropertyInfo(p),
                        p.PropertyType,
                        out typeInfo);

                case MethodInfo m:
                    return TryCreate(
                        new NullableHelper().GetMethodInfo(m).ReturnType,
                        m.ReturnType,
                        out typeInfo);

                default:
                    typeInfo = null;
                    return false;
            }
        }

        public static bool TryCreate(
            IExtendedType type,
            [NotNullWhen(true)] out TypeInfo2? typeInfo) =>
            TryCreate(type, null, out typeInfo);

        private static bool TryCreate(
            IExtendedType type,
            Type? originalType,
            [NotNullWhen(true)]out TypeInfo2? typeInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            originalType ??= type.Type;

            if (SchemaType.TryCreateTypeInfo(
                type,
                originalType,
                out typeInfo))
            {
                return true;
            }

            if (RuntimeType.TryCreateTypeInfo(
                type,
                originalType,
                out typeInfo))
            {
                return true;
            }

            typeInfo = null;
            return false;
        }

        private static bool IsStructureValid(IReadOnlyList<TypeComponentKind> components)
        {
            var nonnull = false;
            var named = false;
            var lists = 0;

            for (var i = 0; i < components.Count; i++)
            {
                if (named)
                {
                    return false;
                }

                switch (components[i])
                {
                    case TypeComponentKind.List:
                        nonnull = false;
                        lists++;

                        if (lists > 2)
                        {
                            return false;
                        }
                        break;

                    case TypeComponentKind.NonNull when nonnull:
                        return false;

                    case TypeComponentKind.NonNull:
                        nonnull = true;
                        break;

                    case TypeComponentKind.Named:
                        nonnull = false;
                        named = true;
                        break;

                    default:
                        throw  new NotSupportedException(
                            "The type component kind is not supported.");
                }
            }

            return named;
        }
    }
}