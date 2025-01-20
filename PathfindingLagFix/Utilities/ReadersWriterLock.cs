using System.Threading;

namespace PathfindingLagFix.Utilities;

internal class ReadersWriterLock
{
    private object conditionVariable = new();

    private int readersActive = 0;
    private int writersWaiting = 0;
    private bool writerActive = false;

    public void BeginRead()
    {
        lock (conditionVariable)
        {
            while (writersWaiting > 0 || writerActive)
                Monitor.Wait(conditionVariable);
            readersActive++;
        }
    }

    public void EndRead()
    {
        lock (conditionVariable)
        {
            readersActive--;
            if (readersActive == 0)
                Monitor.PulseAll(conditionVariable);
        }
    }

    public void BeginWrite()
    {
        lock (conditionVariable)
        {
            writersWaiting++;
            while (readersActive > 0 || writerActive)
                Monitor.Wait(conditionVariable);
            writersWaiting--;
            writerActive = true;
        }
    }

    public void EndWrite()
    {
        lock (conditionVariable)
        {
            writerActive = false;
            Monitor.PulseAll(conditionVariable);
        }
    }
}
