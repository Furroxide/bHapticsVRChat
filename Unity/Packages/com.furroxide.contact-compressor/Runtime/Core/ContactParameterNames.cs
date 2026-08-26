using System;

namespace Furroxide.ContactCompressor
{
    /// <summary>
    /// The wire contract between the avatar and whatever consumes its OSC.
    ///
    /// One float parameter per receiver, named
    /// <c>&lt;prefix&gt;/&lt;RegionId&gt;/&lt;Axis&gt;&lt;Sign&gt;</c>, which VRChat exposes as
    /// <c>/avatar/parameters/&lt;prefix&gt;/&lt;RegionId&gt;/&lt;Axis&gt;&lt;Sign&gt;</c>.
    ///
    /// Example for a torso region encoding all three axes:
    /// <code>
    ///   /avatar/parameters/bOSC/v3/Torso/Xp     float
    ///   /avatar/parameters/bOSC/v3/Torso/Xn     float
    ///   /avatar/parameters/bOSC/v3/Torso/Yp     float
    ///   /avatar/parameters/bOSC/v3/Torso/Yn     float
    ///   /avatar/parameters/bOSC/v3/Torso/Zp     float
    ///   /avatar/parameters/bOSC/v3/Torso/Zn     float
    /// </code>
    ///
    /// Parameters are registered unsynced, so they cost none of the avatar's 256 synced bits and
    /// are evaluated purely on the wearer's client.
    /// </summary>
    public static class ContactParameterNames
    {
        public const string OscAddressPrefix = "/avatar/parameters/";

        /// <summary>Default parameter namespace. Consumers should treat the prefix as configurable.</summary>
        public const string DefaultPrefix = "bOSC/v3";

        /// <summary>Animator parameter name for one receiver of an opposed pair.</summary>
        public static string Parameter(string prefix, string regionId, int axis, bool positive)
        {
            if (string.IsNullOrWhiteSpace(regionId))
                throw new ArgumentException("Region id is required.", nameof(regionId));
            if (axis < 0 || axis > 2)
                throw new ArgumentOutOfRangeException(nameof(axis));

            string p = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim('/');
            return $"{p}/{regionId}/{AxisLetter(axis)}{(positive ? "p" : "n")}";
        }

        /// <summary>Full OSC address for one receiver of an opposed pair.</summary>
        public static string OscAddress(string prefix, string regionId, int axis, bool positive)
            => OscAddressPrefix + Parameter(prefix, regionId, axis, positive);

        /// <summary>
        /// Parses a parameter name (with or without the OSC address prefix) back into its parts.
        /// Returns false for anything that is not a contact-compressor parameter.
        /// </summary>
        public static bool TryParse(string parameterOrAddress, string prefix,
                                    out string regionId, out int axis, out bool positive)
        {
            regionId = null;
            axis = -1;
            positive = false;

            if (string.IsNullOrWhiteSpace(parameterOrAddress)) return false;

            string s = parameterOrAddress;
            if (s.StartsWith(OscAddressPrefix, StringComparison.Ordinal))
                s = s.Substring(OscAddressPrefix.Length);

            string p = (string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim('/')) + "/";
            if (!s.StartsWith(p, StringComparison.Ordinal)) return false;
            s = s.Substring(p.Length);

            int slash = s.LastIndexOf('/');
            if (slash <= 0 || slash != s.Length - 3) return false;

            regionId = s.Substring(0, slash);
            char axisChar = s[slash + 1];
            char signChar = s[slash + 2];

            axis = axisChar == 'X' ? 0 : axisChar == 'Y' ? 1 : axisChar == 'Z' ? 2 : -1;
            if (axis < 0) { regionId = null; return false; }

            if (signChar == 'p') positive = true;
            else if (signChar == 'n') positive = false;
            else { regionId = null; axis = -1; return false; }

            return true;
        }

        public static char AxisLetter(int axis) => axis == 0 ? 'X' : axis == 1 ? 'Y' : 'Z';
    }
}
