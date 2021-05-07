namespace Mpls.Utils
{
    using System;
    using System.Linq;

    public static class RandomBinary
    {
        public static byte[] GenerateRandomBinaryArray(int digits, int numberOfOnes)
        {
            byte[] array = new byte[digits];
            return GenerateRandomBinaryArray(array, numberOfOnes);
        }
        public static byte[] GenerateRandomBinaryArray(byte[] array, int numberOfOnes)
        {
            int countOfOnes = array.Count(c => c == 1);
            return GenerateRandomBinaryArray((byte[])array.Clone(), countOfOnes, numberOfOnes);
        }

        internal static byte[] GenerateRandomBinaryArray(byte[] array, int initialCountOfOnes, int numberOfOnes)
        {
            if (numberOfOnes > array.Length)
            {
                throw new ArgumentException($"The number of 1's could not be more than the total digits for the binary array.");
            }

            Random random = new Random(Guid.NewGuid().GetHashCode());

            byte digit = 1;
            if (initialCountOfOnes > numberOfOnes)
            {
                digit = 0;
            }

            for (int i = 0; i < Math.Abs(numberOfOnes - initialCountOfOnes); i++)
            {
                int index;
                do
                {
                    index = random.Next(0, array.Length);
                }
                while (array[index] == digit);

                array[index] = digit;
            }

            return array;
        }
    }
}