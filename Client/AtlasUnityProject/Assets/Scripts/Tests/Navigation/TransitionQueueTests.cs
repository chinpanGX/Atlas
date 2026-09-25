using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Navigation.Tests
{
    public sealed class TransitionQueueTests
    {
        private GameObject containerObject;
        private Transform container;

        [SetUp]
        public void SetUp()
        {
            containerObject = new GameObject("Container");
            container = containerObject.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (containerObject != null)
            {
                UnityEngine.Object.DestroyImmediate(containerObject);
            }
        }

        [Test]
        public async Task NextTransition_StartsAfterPreviousOneFinishes()
        {
            var queue = TransitionQueue.For(container);
            var log = new List<string>();
            var first = new UniTaskCompletionSource();

            var firstTask = queue.EnqueueAsync(container, async () =>
            {
                log.Add("first start");
                await first.Task;
                log.Add("first end");
            });
            var secondTask = queue.EnqueueAsync(container, () =>
            {
                log.Add("second");
                return UniTask.CompletedTask;
            });

            CollectionAssert.AreEqual(new[] { "first start" }, log);

            first.TrySetResult();
            await firstTask;
            await secondTask;

            CollectionAssert.AreEqual(new[] { "first start", "first end", "second" }, log);
        }

        [Test]
        public async Task FailedTransition_DoesNotBlockNextOne()
        {
            var queue = TransitionQueue.For(container);
            var secondRan = false;

            var firstTask = queue.EnqueueAsync(container, () => UniTask.FromException(new InvalidOperationException("load failed")));
            var secondTask = queue.EnqueueAsync(container, () =>
            {
                secondRan = true;
                return UniTask.CompletedTask;
            });

            Assert.ThrowsAsync<InvalidOperationException>(async () => await firstTask);
            await secondTask;
            Assert.IsTrue(secondRan);
        }

        [Test]
        public async Task Transition_IsCanceledWhenContainerIsDestroyedWhileWaiting()
        {
            var queue = TransitionQueue.For(container);
            var first = new UniTaskCompletionSource();
            var secondRan = false;

            var firstTask = queue.EnqueueAsync(container, () => first.Task);
            var secondTask = queue.EnqueueAsync(container, () =>
            {
                secondRan = true;
                return UniTask.CompletedTask;
            });

            UnityEngine.Object.DestroyImmediate(containerObject);
            first.TrySetResult();
            await firstTask;

            Assert.CatchAsync<OperationCanceledException>(async () => await secondTask);
            Assert.IsFalse(secondRan);
        }

        [Test]
        public void EachContainer_HasItsOwnQueue()
        {
            var other = new GameObject("Other");
            try
            {
                Assert.AreSame(TransitionQueue.For(container), TransitionQueue.For(container));
                Assert.AreNotSame(TransitionQueue.For(container), TransitionQueue.For(other.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }
    }
}
