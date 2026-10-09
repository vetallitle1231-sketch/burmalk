using System;

namespace Burmalk.Models
{
    /// <summary>
    /// Ring buffer of metric samples, oldest first when snapshotted.
    /// NaN values mark unavailable intervals and advance the buffer
    /// (so gaps keep their original time and do not erase past readings).
    /// </summary>
    public class MetricHistory
    {
        private readonly double[] _values;
        private readonly long[] _times;
        private readonly object _gate = new object();
        private int _head;
        private int _count;
        private double _last = double.NaN;

        public MetricHistory(int capacity = 600)
        {
            _values = new double[Math.Max(2, capacity)];
            _times = new long[_values.Length];
        }

        public int Capacity { get { return _values.Length; } }
        public int Count { get { lock (_gate) { return _count; } } }

        /// <summary>Push a value; a NaN value keeps history intact (marks current point invalid).</summary>
        public void Push(double value, long timestampUtc = 0)
        {
            lock (_gate)
            {
                _values[_head] = value;
                _times[_head] = timestampUtc == 0 ? DateTime.UtcNow.Ticks : timestampUtc;
                _head = (_head + 1) % _values.Length;
                if (_count < _values.Length) _count++;
                _last = value;
            }
        }

        public double Current
        {
            get { lock (_gate) { return _last; } }
        }

        /// <summary>Returns values oldest→newest in display-ready arrays: values + availability flags.</summary>
        public int Snapshot(double[] outValues, long[] outTimes = null)
        {
            lock (_gate)
            {
                if (outValues == null) return 0;
                int len = _values.Length;
                int count = _count;
                int start = (_head - count) % len;
                if (start < 0) start += len;
                int i = 0;
                for (int off = 0; off < count; off++)
                {
                    int src = (start + off) % len;
                    if (outValues.Length > i) { outValues[i] = _values[src]; if(outTimes!=null&&outTimes.Length>i)outTimes[i]=_times[src];i++; }
                }
                return i;
            }
        }
    }
}


