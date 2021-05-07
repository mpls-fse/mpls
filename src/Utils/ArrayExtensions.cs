namespace Mpls.Utils
{
    using System;
    using System.Linq;

    public static class ArrayExtensions
    {
        public static T[][] Rotate<T>(this T[][] input)
        {
            int length = input[0].Length;
            T[][] result = new T[length][];

            for (int i = 0; i < length; i++)
            {
                result[i] = input.Select(p => p[i]).ToArray();
            }

            return result;
        }

        public static int GetMaxIndex<T>(this T[] array)
            where T : struct, IComparable<T>
        {
            T? maxValue = null;
            int maxIndex = -1;

            for (int i = 0; i < array.Length; i++)
            {
                T current = array[i];
                if (!maxValue.HasValue || current.CompareTo(maxValue.Value) > 0)
                {
                    maxValue = current;
                    maxIndex = i;
                }
            }

            return maxIndex;
        }

        public static int GetMinIndex<T>(this T[] array)
            where T : struct, IComparable<T>
        {
            T? minValue = null;
            int minIndex = -1;

            for (int i = 0; i < array.Length; i++)
            {
                T current = array[i];
                if (!minValue.HasValue || current.CompareTo(minValue.Value) < 0)
                {
                    minValue = current;
                    minIndex = i;
                }
            }

            return minIndex;
        }

        public static bool IsNullOrEmpty(this Array array)
        {
            return array == null || array.Length == 0;
        }
    }
}