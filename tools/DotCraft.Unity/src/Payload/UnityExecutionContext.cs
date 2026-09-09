using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DotCraft.Unity
{
    public sealed class UnityExecutionContext
    {
        readonly CancellationToken cancellationToken;
        Exception waitError;

        internal UnityExecutionContext(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get { return cancellationToken; } }
        public bool IsCancellationRequested { get { return cancellationToken.IsCancellationRequested; } }
        public UnityAwaitable WaitFrame() { return new UnityAwaitable(this, 1, 0, null); }
        public UnityAwaitable WaitFrames(int frames) { return new UnityAwaitable(this, Math.Max(1, frames), 0, null); }
        public UnityAwaitable WaitSeconds(float seconds) { return new UnityAwaitable(this, 1, Math.Max(0, seconds), null); }
        public UnityAwaitable WaitUntil(Func<bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException("predicate");
            return new UnityAwaitable(this, 1, 0, predicate);
        }
        public void ThrowIfCancellationRequested()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (waitError == null) return;
            var error = waitError;
            waitError = null;
            throw error;
        }
        internal bool Evaluate(Func<bool> predicate)
        {
            if (predicate == null) return true;
            try { return predicate(); }
            catch (Exception e) { waitError = e; return true; }
        }
        internal void Schedule(Action continuation, int frames, double seconds, Func<bool> predicate)
        {
            ThrowIfCancellationRequested();
            Bridge.ScheduleContinuation(this, continuation, frames, seconds, predicate);
        }
    }

    public struct UnityAwaitable
    {
        readonly UnityExecutionContext context;
        readonly int frames;
        readonly double seconds;
        readonly Func<bool> predicate;

        internal UnityAwaitable(UnityExecutionContext context, int frames, double seconds, Func<bool> predicate)
        {
            this.context = context;
            this.frames = frames;
            this.seconds = seconds;
            this.predicate = predicate;
        }

        public Awaiter GetAwaiter() { return new Awaiter(context, frames, seconds, predicate); }

        public struct Awaiter : INotifyCompletion
        {
            readonly UnityExecutionContext context;
            readonly int frames;
            readonly double seconds;
            readonly Func<bool> predicate;
            internal Awaiter(UnityExecutionContext context, int frames, double seconds, Func<bool> predicate)
            {
                this.context = context;
                this.frames = frames;
                this.seconds = seconds;
                this.predicate = predicate;
            }
            public bool IsCompleted { get { return false; } }
            public void OnCompleted(Action continuation) { context.Schedule(continuation, frames, seconds, predicate); }
            public void GetResult() { context.ThrowIfCancellationRequested(); }
        }
    }
}
