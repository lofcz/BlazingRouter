using System;
using System.Diagnostics;
using System.Threading;

namespace RadixRouter.SourceGenerator.External;

internal sealed class ObjectPool<T>
    where T : class
{
    // Storage for the pool objects. The first item is stored in a dedicated field because we
    // expect to be able to satisfy most requests from it.
    private T? firstItem;
    private readonly Element[] items;

    // The factory is stored for the lifetime of the pool. We will call this only when pool needs to
    // expand. compared to "new T()", Func gives more flexibility to implementers and faster
    // than "new T()".
    private readonly Func<T> factory; 

    /// <summary>
    /// Creates a new <see cref="ObjectPool{T}"/> instance with a given factory.
    /// </summary>
    /// <param name="factory">The factory to use to produce new objects.</param>
    public ObjectPool(Func<T> factory)
        : this(factory, Environment.ProcessorCount * 2)
    {
    }

    /// <summary>
    /// Creates a new <see cref="ObjectPool{T}"/> instance with a given factory.
    /// </summary>
    /// <param name="factory">The factory to use to produce new objects.</param>
    /// <param name="size">The size of the pool.</param>
    public ObjectPool(Func<T> factory, int size)
    {
        this.factory = factory;
        this.items = new Element[size - 1];
    }

    /// <summary>
    /// Produces an instance.
    /// </summary>
    /// <returns>The instance to return to the pool later.</returns>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically 
    /// reducing how far we will typically search.
    /// </remarks>
    public T Allocate()
    {
        // PERF: Examine the first element. If that fails, AllocateSlow will look at the remaining elements.
        // Note that the initial read is optimistically not synchronized. That is intentional. 
        // We will interlock only when we have a candidate. in a worst case we may miss some
        // recently returned objects. Not a big deal.
        T? instance = this.firstItem;
        if (instance == null || instance != Interlocked.CompareExchange(ref this.firstItem, null, instance))
        {
            instance = AllocateSlow();
        }

        return instance;
    }

    /// <summary>
    /// Slow path to produce a new instance.
    /// </summary>
    /// <returns>The instance to return to the pool later.</returns>
    private T AllocateSlow()
    {
        Element[] items = this.items;

        for (int i = 0; i < items.Length; i++)
        {
            T? instance = items[i].Value;

            if (instance is not null)
            {
                if (instance == Interlocked.CompareExchange(ref items[i].Value, null, instance))
                {
                    return instance;
                }
            }
        }

        return this.factory();
    }

    /// <summary>
    /// Returns objects to the pool.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically 
    /// reducing how far we will typically search in Allocate.
    /// </remarks>
    public void Free(T obj)
    {
        if (this.firstItem is null)
        {
            this.firstItem = obj;
        }
        else
        {
            FreeSlow(obj);
        }
    }

    /// <summary>
    /// Slow path to return an object to the pool.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    private void FreeSlow(T obj)
    {
        Element[] items = this.items;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Value == null)
            {
                items[i].Value = obj;

                break;
            }
        }
    }

    /// <summary>
    /// Wrapper to avoid array covariance.
    /// </summary>
    [DebuggerDisplay("{Value,nq}")]
    private struct Element
    {
        /// <summary>
        /// The value for the current element.
        /// </summary>
        public T? Value;
    }
}