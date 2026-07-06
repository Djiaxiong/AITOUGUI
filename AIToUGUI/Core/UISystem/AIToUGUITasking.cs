using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed class Awaiter<T> : INotifyCompletion
{
    private bool _isCompleted;
    private T _result;
    private Exception _exception;
    private Action _continuation;

    public bool IsCompleted => _isCompleted;

    public Awaiter<T> GetAwaiter()
    {
        return this;
    }

    public void OnCompleted(Action continuation)
    {
        if (_isCompleted)
        {
            continuation?.Invoke();
            return;
        }

        _continuation += continuation;
    }

    public T GetResult()
    {
        if (!_isCompleted)
        {
            throw new InvalidOperationException("Awaiter result was requested before completion.");
        }

        if (_exception != null)
        {
            throw _exception;
        }

        return _result;
    }

    internal void SetResult(T result)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        _result = result;
        InvokeContinuation();
    }

    internal void SetException(Exception exception)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        _exception = exception ?? new Exception("AIToUGUI async task failed.");
        InvokeContinuation();
    }

    private void InvokeContinuation()
    {
        var continuation = _continuation;
        _continuation = null;
        continuation?.Invoke();
    }
}

public static class MiniTask
{
    public static Awaiter<T> FromCoroutine<T>(Func<Action<T>, IEnumerator> coroutineFactory)
    {
        var awaiter = new Awaiter<T>();
        AIToUGUITaskRunner.Instance.Run(coroutineFactory, awaiter);
        return awaiter;
    }

    public static Awaiter<T> FromResult<T>(T result)
    {
        var awaiter = new Awaiter<T>();
        awaiter.SetResult(result);
        return awaiter;
    }

    public static Awaiter<T> FromException<T>(Exception exception)
    {
        var awaiter = new Awaiter<T>();
        awaiter.SetException(exception);
        return awaiter;
    }
}

internal sealed class AIToUGUITaskRunner : BaseMonoManager<AIToUGUITaskRunner>
{
    public override void Init()
    {
        gameObject.hideFlags = HideFlags.HideInHierarchy;
    }

    public void Run<T>(Func<Action<T>, IEnumerator> coroutineFactory, Awaiter<T> awaiter)
    {
        StartCoroutine(RunInternal(coroutineFactory, awaiter));
    }

    private static IEnumerator RunInternal<T>(Func<Action<T>, IEnumerator> coroutineFactory, Awaiter<T> awaiter)
    {
        var completed = false;
        IEnumerator routine = null;

        void SetResult(T result)
        {
            completed = true;
            awaiter.SetResult(result);
        }

        try
        {
            if (coroutineFactory != null)
            {
                routine = coroutineFactory(SetResult);
            }
        }
        catch (Exception exception)
        {
            awaiter.SetException(exception);
            yield break;
        }

        if (routine != null)
        {
            while (true)
            {
                object current = null;
                bool movedNext;

                try
                {
                    movedNext = routine.MoveNext();
                    if (movedNext)
                    {
                        current = routine.Current;
                    }
                }
                catch (Exception exception)
                {
                    awaiter.SetException(exception);
                    yield break;
                }

                if (!movedNext)
                {
                    break;
                }

                yield return current;
            }
        }

        if (!completed)
        {
            awaiter.SetResult(default);
        }
    }
}
