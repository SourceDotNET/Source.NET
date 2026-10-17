using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Common;

public struct Functor : IRefCounted, IDisposable
{
	public readonly int AddRef() => throw new NotImplementedException();
	public readonly int Release() => throw new NotImplementedException();

	public void Dispose() { }

	public uint UserID;
}

public interface ICallQueue
{
	public void QueueFunctor(ref Functor functor) {

	}
	void QueueFunctorInternal(ref Functor functor);

	void QueueCall(Action func);
	void QueueCall<T>(Action<T> func, in T args);
}
