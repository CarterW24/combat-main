using System.Numerics;

namespace Sanctuary.UdpLibrary.Internal;

public class PriorityQueueMember
{
    internal int PriorityQueuePosition = -1;
}

internal class PriorityQueue<T, P> where T : PriorityQueueMember where P : INumber<P>
{
    private struct QueueEntry
    {
        public T entry;
        public P priority;
    }

    private QueueEntry[] Queue;
    private int QueueSize;
    private int QueueEnd;

    public PriorityQueue(int queueSize)
    {
        QueueEnd = 0;
        QueueSize = queueSize;
        Queue = new QueueEntry[QueueSize];
    }

    public T? Top()
    {
        return QueueEnd == 0 ? null : Queue[0].entry;
    }

    public T? TopRemove()
    {
        if (QueueEnd == 0)
            return null;

        var top = Queue[0].entry;

        Remove(top);

        return top;
    }

    public T? TopRemove(P priority)
    {
        return QueueEnd > 0 && Queue[0].priority <= priority ? Remove(Queue[0].entry) : null;
    }

    public T? Add(T entry, P priority)
    {
        if (entry.PriorityQueuePosition == -1)
        {
            if (QueueEnd >= QueueSize)
                return null;

            Queue[QueueEnd].entry = entry;
            Queue[QueueEnd].priority = priority;
            Queue[QueueEnd].entry.PriorityQueuePosition = QueueEnd;

            QueueEnd++;
        }
        else
        {
            if (Queue[entry.PriorityQueuePosition].priority == priority)
                return entry;

            Queue[entry.PriorityQueuePosition].priority = priority;
        }

        Refloat(entry);

        return entry;
    }

    public T Remove(T entry)
    {
        if (entry.PriorityQueuePosition == -1)
            return entry;

        QueueEnd--;

        var spot = entry.PriorityQueuePosition;

        if (spot != QueueEnd)
        {
            Queue[spot] = Queue[QueueEnd];
            Queue[spot].entry.PriorityQueuePosition = spot;

            Refloat(Queue[spot].entry);
        }

        entry.PriorityQueuePosition = -1;

        return entry;
    }

    public P? GetPriority(T entry)
    {
        return entry.PriorityQueuePosition >= 0 ? Queue[entry.PriorityQueuePosition].priority : default;
    }

    public int QueueUsed()
    {
        return QueueEnd;
    }

    private void Refloat(T entry)
    {
        var spot = entry.PriorityQueuePosition;

        var tryDown = true;

        while (spot > 0 && Queue[spot].priority < Queue[(spot - 1) / 2].priority)
        {
            var newSpot = (spot - 1) / 2;

            var hold = Queue[spot];

            Queue[spot] = Queue[newSpot];
            Queue[spot].entry.PriorityQueuePosition = spot;

            Queue[newSpot] = hold;
            Queue[newSpot].entry.PriorityQueuePosition = newSpot;

            spot = newSpot;

            tryDown = false;
        }

        if (tryDown)
        {
            while (true)
            {
                var downSpot1 = spot * 2 + 1;

                if (downSpot1 >= QueueEnd)
                    break;

                var downSpot2 = spot * 2 + 2;

                if (downSpot2 >= QueueEnd || Queue[downSpot1].priority < Queue[downSpot2].priority)
                {
                    if (Queue[downSpot1].priority >= Queue[spot].priority)
                        break;

                    var hold = Queue[spot];

                    Queue[spot] = Queue[downSpot1];
                    Queue[spot].entry.PriorityQueuePosition = spot;

                    Queue[downSpot1] = hold;
                    Queue[downSpot1].entry.PriorityQueuePosition = downSpot1;

                    spot = downSpot1;
                }
                else
                {
                    if (Queue[downSpot2].priority >= Queue[spot].priority)
                        break;

                    var hold = Queue[spot];

                    Queue[spot] = Queue[downSpot2];
                    Queue[spot].entry.PriorityQueuePosition = spot;

                    Queue[downSpot2] = hold;
                    Queue[downSpot2].entry.PriorityQueuePosition = downSpot2;

                    spot = downSpot2;
                }
            }
        }
    }
}
