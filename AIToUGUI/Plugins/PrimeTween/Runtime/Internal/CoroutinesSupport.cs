using System;
using System.Collections;
using JetBrains.Annotations;
using UnityEngine;

namespace PrimeTween {
    public partial struct Tween : IEnumerator {
        /// <summary>Use this method to wait for an animation in coroutines.<br/>
        /// NOTE: stopping a coroutine early with StopCoroutine() (or destroying the parent MonoBehaviour) will prevent PrimeTween from reusing the returned IEnumerator and will leave it to GC to clean.<br/>
        /// This means that stopping the coroutine while it's waiting for an animation will produce a small amount of GC garbage. Consider using `while (animation.isAlive) yield return null;` instead to prevent allocations in this case.</summary>
        /// <example><code>
        /// IEnumerator Coroutine() {
        ///     yield return Tween.Delay(1).ToYieldInstruction();
        /// }
        /// </code></example>
        [NotNull]
        public IEnumerator ToYieldInstruction() {
            if (!isAlive || !TryManipulate()) {
                return Array.Empty<object>().GetEnumerator();
            }

            CoroutineIterator result;
            var pool = PrimeTweenManager.Instance._coroutineIterators;
            if (pool.Count > 0) {
                result = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            } else {
                result = new CoroutineIterator();
            }
            Assert.IsFalse(result._tween.IsCreated);
            result._tween = this;
            return result;
        }

        bool IEnumerator.MoveNext() {
            PrimeTweenManager.Instance.WarnStructBoxingInCoroutineOnce(id, tween);
            return isAlive;
        }

        object IEnumerator.Current {
            get {
                Assert.IsTrue(isAlive);
                return null;
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }

    internal class CoroutineIterator : IEnumerator {
        internal Tween _tween;

        bool IEnumerator.MoveNext() {
            if (!_tween.IsCreated) {
                Debug.LogError(Constants.coroutineFinishedError);
                return false;
            }
            if (_tween.isAlive) {
                return true;
            }
            // Return to pool only when a coroutine is iterated to the end. Else, leave the CoroutineIterator to be cleaned by GC
            _tween = default;
            PrimeTweenManager.Instance._coroutineIterators.Add(this);
            return false;
        }

        object IEnumerator.Current {
            get {
                Assert.IsTrue(_tween.isAlive);
                return null;
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }

    public partial struct Sequence : IEnumerator {
        /// <inheritdoc cref="Tween.ToYieldInstruction"/>
        /// <example><code>
        /// IEnumerator Coroutine() {
        ///     var sequence = Sequence.Create(Tween.Delay(1)).ChainCallback(() =&gt; Debug.Log("Done!"));
        ///     yield return sequence.ToYieldInstruction();
        /// }
        /// </code></example>
        [NotNull]
        public IEnumerator ToYieldInstruction() => root.ToYieldInstruction();

        bool IEnumerator.MoveNext() {
            PrimeTweenManager.Instance.WarnStructBoxingInCoroutineOnce(id, root.tween);
            return isAlive;
        }

        object IEnumerator.Current {
            get {
                Assert.IsTrue(isAlive);
                return null;
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
