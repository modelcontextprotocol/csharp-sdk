using System.Text;

namespace ModelContextProtocol.Utils
{
    /// <summary>
    /// Helper methods for Base64Url encoding and decoding.
    /// Based on implementation from MSAL (https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/main/src/client/Microsoft.Identity.Client/Utils/Base64UrlHelpers.cs)
    /// </summary>
    internal static class Base64UrlHelpers
    {
        private const char base64UrlCharacter62 = '-';
        private const char base64UrlCharacter63 = '_';

        /// <summary>
        /// Encoding table for base64url encoding
        /// </summary>
        internal static readonly char[] s_base64Table =
        {
            'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z',
            'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z',
            '0','1','2','3','4','5','6','7','8','9',
            base64UrlCharacter62,
            base64UrlCharacter63
        };

        /// <summary>
        /// Converts an array of bytes to a base64url encoded string.
        /// </summary>
        /// <param name="inArray">An array of 8-bit unsigned integers.</param>
        /// <returns>The string representation in base64url encoding of inArray.</returns>
        public static string Encode(byte[] inArray)
        {
            if (inArray == null)
                return string.Empty;

            return Encode(inArray, 0, inArray.Length);
        }

        /// <summary>
        /// Converts a subset of an array of 8-bit unsigned integers to its equivalent string representation that is encoded with base-64-url digits.
        /// </summary>
        /// <param name="inArray">An array of 8-bit unsigned integers.</param>
        /// <param name="offset">An offset in inArray.</param>
        /// <param name="length">The number of elements of inArray to convert.</param>
        /// <returns>The string representation in base64url encoding of length elements of inArray, starting at position offset.</returns>
        private static string Encode(byte[] inArray, int offset, int length)
        {
            _ = inArray ?? throw new ArgumentNullException(nameof(inArray));

            if (length == 0)
                return string.Empty;

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (offset < 0 || inArray.Length < offset)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (inArray.Length < offset + length)
                throw new ArgumentOutOfRangeException(nameof(length));

            int lengthmod3 = length % 3;
            int limit = offset + (length - lengthmod3);
            char[] output = new char[(length + 2) / 3 * 4];
            char[] table = s_base64Table;
            int i, j = 0;

            // Process three bytes at a time, each three bytes becomes four base64 characters
            for (i = offset; i < limit; i += 3)
            {
                byte d0 = inArray[i];
                byte d1 = inArray[i + 1];
                byte d2 = inArray[i + 2];

                output[j + 0] = table[d0 >> 2];
                output[j + 1] = table[((d0 & 0x03) << 4) | (d1 >> 4)];
                output[j + 2] = table[((d1 & 0x0f) << 2) | (d2 >> 6)];
                output[j + 3] = table[d2 & 0x3f];
                j += 4;
            }

            // Handle remaining bytes and padding
            i = limit;

            switch (lengthmod3)
            {
                case 2:
                    {
                        byte d0 = inArray[i];
                        byte d1 = inArray[i + 1];

                        output[j + 0] = table[d0 >> 2];
                        output[j + 1] = table[((d0 & 0x03) << 4) | (d1 >> 4)];
                        output[j + 2] = table[(d1 & 0x0f) << 2];
                        j += 3;
                    }
                    break;

                case 1:
                    {
                        byte d0 = inArray[i];

                        output[j + 0] = table[d0 >> 2];
                        output[j + 1] = table[(d0 & 0x03) << 4];
                        j += 2;
                    }
                    break;

                    // Default or case 0: no further operations are needed.
            }

            // Return the result without creating any additional string allocations
            return new string(output, 0, j);
        }

        /// <summary>
        /// Encodes a string using base64url encoding.
        /// </summary>
        /// <param name="str">The string to encode.</param>
        /// <returns>Base64Url encoding of the UTF8 bytes of the input string.</returns>
        public static string EncodeString(string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str), "Input string cannot be null.");

            return Encode(Encoding.UTF8.GetBytes(str)) ?? string.Empty;
        }
    }
}