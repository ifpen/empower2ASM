using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MillenniumToolkit;

namespace Empower2Asm.Empower
{
    /// <summary>Small helpers to drive the Toolkit's viewer objects. Call only on the Toolkit thread.</summary>
    internal static class Viewers
    {
        /// <summary>Every field Empower knows for a field type.</summary>
        public static Fields AllFields(mtkFieldType type)
        {
            var fields = new Fields { Type = type };
            fields.Fill();
            return fields;
        }

        /// <summary>A field collection with the given names.</summary>
        public static Fields FieldsOf(mtkFieldType type, IEnumerable<string> names)
        {
            var fields = new Fields { Type = type };
            foreach (var name in names)
            {
                fields.Add(name);
            }

            return fields;
        }

        public static IReadOnlyList<string> Names(Fields fields) =>
            fields.Cast<object>().Select(f => f.ToString()).ToList();

        public static Ids IdsOf(params int[] ids)
        {
            var collection = new Ids();
            foreach (var id in ids)
            {
                collection.Add(id);
            }

            return collection;
        }

        /// <summary>One search condition, combined with the previous ones by AND.</summary>
        public static SearchField Condition(mtkFieldType type, string name, mtkOperator op, object condition) =>
            new SearchField
            {
                Type = type,
                Name = name,
                Operator = op,
                Condition = System.Convert.ToString(condition, CultureInfo.InvariantCulture),
                Logic = mtkLogic.mtkAnd,
            };

        /// <summary>
        /// Makes the viewer fetch <paramref name="fields"/> found by the conditions, combined by AND. The viewer takes
        /// <paramref name="fields"/>, with each searched field added; every condition is on that field type.
        /// </summary>
        public static void Search(InjectionViewer viewer, Fields fields, params SearchField[] conditions)
        {
            viewer.Fields = Fetching(fields, conditions);
            viewer.Criteria = mtkCriteria.mtkSearch;
            viewer.SearchFields = AllOf(conditions);
        }

        /// <inheritdoc cref="Search(InjectionViewer, Fields, SearchField[])"/>
        public static void Search(ChannelViewer viewer, Fields fields, params SearchField[] conditions)
        {
            viewer.Fields = Fetching(fields, conditions);
            viewer.Criteria = mtkCriteria.mtkSearch;
            viewer.SearchFields = AllOf(conditions);
        }

        /// <inheritdoc cref="Search(InjectionViewer, Fields, SearchField[])"/>
        public static void Search(ResultViewer viewer, Fields fields, params SearchField[] conditions)
        {
            viewer.Fields = Fetching(fields, conditions);
            viewer.Criteria = mtkCriteria.mtkSearch;
            viewer.SearchFields = AllOf(conditions);
        }

        /// <summary>
        /// The fields plus every field a condition searches. The Toolkit refuses a search on a field its viewer doesn't
        /// fetch: Fetch fails with "Invalid value" (0x80040203), whatever the user's privileges.
        /// </summary>
        private static Fields Fetching(Fields fields, SearchField[] conditions)
        {
            var names = new HashSet<string>(Names(fields), StringComparer.OrdinalIgnoreCase);
            foreach (var condition in conditions)
            {
                if (condition.Type != fields.Type)
                {
                    throw new ArgumentException($"The search on {condition.Type} field {condition.Name} isn't on the viewer's {fields.Type} fields.", nameof(conditions));
                }

                if (names.Add(condition.Name))
                {
                    fields.Add(condition.Name);
                }
            }

            return fields;
        }

        private static SearchFields AllOf(SearchField[] conditions)
        {
            var collection = new SearchFields();
            foreach (var condition in conditions)
            {
                collection.Add(condition);
            }

            return collection;
        }
    }
}
