using System;
using System.Collections.Generic;

namespace CrystalLeather
{
    // The game's own lookup by name never finds "Wyrm Face Leather" (it lowercases the table but not
    // what you ask for), so search the list ourselves.
    public static class NameLookup
    {
        public static string Key(string name)
        {
            return name == null ? null : name.Replace(" ", "").ToLowerInvariant();
        }

        public static T Find<T>(IEnumerable<T> items, Func<T, string> nameOf, string name) where T : class
        {
            string want = Key(name);
            if (want == null) return null;
            foreach (T item in items)
                if (item != null && Key(nameOf(item)) == want) return item;
            return null;
        }
    }
}
