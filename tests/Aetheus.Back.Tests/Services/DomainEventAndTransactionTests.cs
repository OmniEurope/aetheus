// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Services;
using Aetheus.Back.Services.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Back.Tests.Services;

/// <summary>
/// Tests for DomainEventDispatcher, DomainEventAuditHandler, and DbTransactionScopeExtensions.
/// </summary>
public class DomainEventAndTransactionTests
{
    // --- Test helpers ---

    private record TestEvent(string Name) : IDomainEvent;

    private class FailingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("Handler failed");
    }

    private class TrackingHandler : IDomainEventHandler<TestEvent>
    {
        public List<TestEvent> Received { get; } = [];
        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    // --- DomainEventDispatcher.DispatchAsync ---

    [Fact]
    public async Task DispatchAsync_InvokesAllHandlers()
    {
        var handler1 = new TrackingHandler();
        var handler2 = new TrackingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler2);
        var sp = services.BuildServiceProvider();

        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(sp, queue, NullLogger<DomainEventDispatcher>.Instance);

        var evt = new TestEvent("test-event");
        await dispatcher.DispatchAsync(evt, ct: TestContext.Current.CancellationToken);

        Assert.Single(handler1.Received);
        Assert.Single(handler2.Received);
        Assert.Equal("test-event", handler1.Received[0].Name);
    }

    [Fact]
    public async Task DispatchAsync_HandlerFailure_DoesNotAbortChain()
    {
        var tracking = new TrackingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(new FailingHandler());
        services.AddSingleton<IDomainEventHandler<TestEvent>>(tracking);
        var sp = services.BuildServiceProvider();

        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(sp, queue, NullLogger<DomainEventDispatcher>.Instance);

        await dispatcher.DispatchAsync(new TestEvent("after-fail"), ct: TestContext.Current.CancellationToken);

        Assert.Single(tracking.Received);
        Assert.Equal("after-fail", tracking.Received[0].Name);
    }

    [Fact]
    public async Task DispatchAsync_NullEvent_Throws()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(services, queue, NullLogger<DomainEventDispatcher>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync<TestEvent>(null!, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_NoHandlers_CompletesSuccessfully()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(services, queue, NullLogger<DomainEventDispatcher>.Instance);

        await dispatcher.DispatchAsync(new TestEvent("orphan"), ct: TestContext.Current.CancellationToken);
    }

    // --- DomainEventDispatcher.Publish ---

    [Fact]
    public void Publish_EnqueuesWorkItem()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(services, queue, NullLogger<DomainEventDispatcher>.Instance);

        dispatcher.Publish(new TestEvent("queued"));

        Assert.Equal(1, queue.CurrentLength);
    }

    [Fact]
    public void Publish_NullEvent_Throws()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var queue = new BackgroundTaskQueue();
        var dispatcher = new DomainEventDispatcher(services, queue, NullLogger<DomainEventDispatcher>.Instance);

        Assert.Throws<ArgumentNullException>(() => dispatcher.Publish<TestEvent>(null!));
    }

    // --- DomainEventAuditHandler ---

    [Fact]
    public async Task AuditHandler_LogsEventToAuditService()
    {
        var auditMock = Substitute.For<IAuditService>();
        var handler = new DomainEventAuditHandler<TestEvent>(
            auditMock, NullLogger<DomainEventAuditHandler<TestEvent>>.Instance);

        var evt = new TestEvent("audit-me");
        await handler.HandleAsync(evt, ct: TestContext.Current.CancellationToken);

        await auditMock.Received(1).LogAsync(
            "DomainEvent",
            "TestEvent",
            null,
            Arg.Is<string>(s => s.Contains("audit-me")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditHandler_NullEvent_ReturnsEarly()
    {
        var auditMock = Substitute.For<IAuditService>();
        var handler = new DomainEventAuditHandler<TestEvent>(
            auditMock, NullLogger<DomainEventAuditHandler<TestEvent>>.Instance);

        await handler.HandleAsync(null!, ct: TestContext.Current.CancellationToken);

        await auditMock.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditHandler_AuditFailure_DoesNotThrow()
    {
        var auditMock = Substitute.For<IAuditService>();
        auditMock.LogAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var handler = new DomainEventAuditHandler<TestEvent>(
            auditMock, NullLogger<DomainEventAuditHandler<TestEvent>>.Instance);

        await handler.HandleAsync(new TestEvent("safe"), ct: TestContext.Current.CancellationToken);
    }

    // --- DbTransactionScopeExtensions ---

    [Fact]
    public async Task ExecuteInTransaction_Success_CommitsTransaction()
    {
        var txMock = Substitute.For<IDbTransactionScope>();
        var executed = false;

        await txMock.ExecuteInTransactionAsync(async () =>
        {
            executed = true;
            await Task.CompletedTask;
        }, ct: TestContext.Current.CancellationToken);

        Assert.True(executed);
        await txMock.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await txMock.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await txMock.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransaction_Failure_RollsBack()
    {
        var txMock = Substitute.For<IDbTransactionScope>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            txMock.ExecuteInTransactionAsync(() =>
                throw new InvalidOperationException("boom"), ct: TestContext.Current.CancellationToken));

        await txMock.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await txMock.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await txMock.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransaction_NullTx_Throws()
    {
        IDbTransactionScope tx = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            tx.ExecuteInTransactionAsync(() => Task.CompletedTask, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteInTransaction_NullWork_Throws()
    {
        var txMock = Substitute.For<IDbTransactionScope>();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            txMock.ExecuteInTransactionAsync((Func<Task>)null!, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteInTransactionT_Success_ReturnsResult()
    {
        var txMock = Substitute.For<IDbTransactionScope>();

        var result = await txMock.ExecuteInTransactionAsync(async () =>
        {
            await Task.CompletedTask;
            return 42;
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        await txMock.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await txMock.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransactionT_Failure_RollsBack()
    {
        var txMock = Substitute.For<IDbTransactionScope>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            txMock.ExecuteInTransactionAsync<int>(() =>
                throw new InvalidOperationException("boom"), ct: TestContext.Current.CancellationToken));

        await txMock.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await txMock.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransactionT_NullTx_Throws()
    {
        IDbTransactionScope tx = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            tx.ExecuteInTransactionAsync(() => Task.FromResult(1), ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteInTransactionT_NullWork_Throws()
    {
        var txMock = Substitute.For<IDbTransactionScope>();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            txMock.ExecuteInTransactionAsync((Func<Task<int>>)null!, ct: TestContext.Current.CancellationToken));
    }
}
