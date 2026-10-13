using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Haugbui.Core.GameApi
{
    public sealed class GameApiReport
    {
        public GameApiReport(int checkedCount, IReadOnlyList<ApiMember> missing)
        {
            CheckedCount = checkedCount;
            Missing = missing;
        }

        public int CheckedCount { get; }
        public IReadOnlyList<ApiMember> Missing { get; }
        public bool Passed => Missing.Count == 0;

        public IReadOnlyCollection<string> DisabledFeatures =>
            new HashSet<string>(Missing.Select(m => m.Feature));

        public bool IsFeatureAvailable(string feature) => Missing.All(m => m.Feature != feature);
    }

    /// <summary>
    /// Works on any <see cref="Type"/>, so the same rules run on live game types and on metadata-only types in tests.
    /// </summary>
    public static class GameApiChecker
    {
        private const BindingFlags All =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static GameApiReport Check(IEnumerable<ApiMember> members, Func<string, string, Type> resolveType)
        {
            var missing = new List<ApiMember>();
            int count = 0;
            foreach (ApiMember member in members)
            {
                count++;
                Type type = SafeResolve(resolveType, member);
                if (type == null || (member.Kind != MemberKind.Type && !HasMember(type, member)))
                    missing.Add(member);
            }
            return new GameApiReport(count, missing);
        }

        private static Type SafeResolve(Func<string, string, Type> resolveType, ApiMember member)
        {
            try
            {
                return resolveType(member.Assembly, member.Type);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HasMember(Type type, ApiMember member)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                switch (member.Kind)
                {
                    case MemberKind.Field:
                        if (current.GetFields(All).Any(f => f.Name == member.Member)) return true;
                        break;
                    case MemberKind.Property:
                        if (current.GetProperties(All).Any(p => p.Name == member.Member)) return true;
                        break;
                    case MemberKind.Method:
                        if (current.GetMethods(All).Any(m => m.Name == member.Member && ParametersMatch(m, member))) return true;
                        break;
                }
            }
            return false;
        }

        private static bool ParametersMatch(MethodInfo method, ApiMember member)
        {
            if (member.ParameterTypes.Count == 0) return true;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == member.ParameterTypes.Count
                   && parameters.Select(p => p.ParameterType.FullName).SequenceEqual(member.ParameterTypes);
        }
    }
}
