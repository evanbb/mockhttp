﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RichardSzalay.MockHttp
{
    /// <summary>
    /// Responds to requests using pre-configured responses
    /// </summary>
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private Queue<IMockedRequest> requestExpectations = new Queue<IMockedRequest>();
        private List<IMockedRequest> backendDefinitions = new List<IMockedRequest>();

        private int outstandingRequests = 0;

        public MockHttpMessageHandler()
        {
            AutoFlush = true;
        }

        private bool autoFlush;

        /// <summary>
        /// Requests received while AutoFlush is true will complete instantly. 
        /// Requests received while AutoFlush is false will not complete until <see cref="M:Flush"/> is called
        /// </summary>
        public bool AutoFlush
        {
            get
            {
                return autoFlush;
            }
            set
            {
                autoFlush = value;

                if (autoFlush)
                {
                    flusher = new TaskCompletionSource<object>();
                    flusher.SetResult(null);
                }
                else
                {
                    flusher = new TaskCompletionSource<object>();
                    pendingFlushers.Enqueue(flusher);
                }
            }
        }

        private Queue<TaskCompletionSource<object>> pendingFlushers = new Queue<TaskCompletionSource<object>>();
        private TaskCompletionSource<object> flusher;

        /// <summary>
        /// Completes all pendings requests that were received while <see cref="M:AutoComplete"/> was true
        /// </summary>
        public void Flush()
        {
            while (pendingFlushers.Count > 0)
                pendingFlushers.Dequeue().SetResult(null);
        }

        /// <summary>
        /// Completes <param name="count" /> pendings requests that were received while <see cref="M:AutoComplete"/> was true
        /// </summary>
        public void Flush(int count)
        {
            while (pendingFlushers.Count > 0 && count-- > 0)
                pendingFlushers.Dequeue().SetResult(null);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requestExpectations.Count > 0)
            {
                var handler = requestExpectations.Peek();

                if (handler.Matches(request))
                {
                    requestExpectations.Dequeue();

                    return SendAsync(handler, request, cancellationToken);
                }
            }
            else
            {
                foreach (var handler in backendDefinitions)
                {
                    if (handler.Matches(request))
                    {
                        return SendAsync(handler, request, cancellationToken);
                    }
                }
            }

            return TaskEx.FromResult(FallbackResponse);
        }

        private Task<HttpResponseMessage> SendAsync(IMockedRequest handler, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref outstandingRequests);

            if (!AutoFlush)
            {
                flusher = new TaskCompletionSource<object>();
                pendingFlushers.Enqueue(flusher);
            }

            return flusher.Task.ContinueWith(_ =>
                {
                    Interlocked.Decrement(ref outstandingRequests);

                    return handler.SendAsync(request, cancellationToken);
                }).Unwrap();
        }

        private HttpResponseMessage fallbackResponse = null;

        /// <summary>
        /// Gets or sets the response that will be returned for requests that were not matched
        /// </summary>
        public HttpResponseMessage FallbackResponse
        {
            get
            {
                return fallbackResponse ?? CreateDefaultFallbackMessage();
            }
            set
            {
                fallbackResponse = value;
            }
        }

        HttpResponseMessage CreateDefaultFallbackMessage()
        {
            HttpResponseMessage message = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            message.ReasonPhrase = "No matching mock handler";
            return message;
        }

        /// <summary>
        /// Adds a request expectation
        /// </summary>
        /// <remarks>
        /// Request expectations:
        /// 
        /// <list>
        /// <item>Match once</item>
        /// <item>Match in order</item>
        /// <item>Match before any backend definitions</item>
        /// </list>
        /// </remarks>
        /// <param name="handler">The <see cref="T:IMockedRequest"/> that will handle the request</param>
        public void AddRequestExpectation(IMockedRequest handler)
        {
            requestExpectations.Enqueue(handler);
        }

        /// <summary>
        /// Adds a backend definition
        /// </summary>
        /// <remarks>
        /// Backend definitions:
        /// 
        /// <list>
        /// <item>Match multiple times</item>
        /// <item>Match in any order</item>
        /// <item>Match after all request expectations have been met</item>
        /// </list>
        /// </remarks>
        /// <param name="handler">The <see cref="T:IMockedRequest"/> that will handle the request</param>
        public void AddBackendDefinition(IMockedRequest handler)
        {
            backendDefinitions.Add(handler);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// Throws an <see cref="T:InvalidOperationException"/> if there are requests that were received 
        /// while <see cref="M:AutoFlush"/> was true, but have not been completed using <see cref="M:Flush"/>
        /// </summary>
        public void VerifyNoOutstandingRequest()
        {
            if (outstandingRequests > 0)
                throw new InvalidOperationException("There are " + outstandingRequests + " oustanding requests. Call Flush() to complete them");
        }

        /// <summary>
        /// Throws an <see cref="T:InvalidOperationException"/> if there are any requests configured with Expects 
        /// that have yet to be received
        /// </summary>
        public void VerifyNoOutstandingExpectation()
        {
            if (this.requestExpectations.Count > 0)
                throw new InvalidOperationException("There are " + requestExpectations.Count + " unfulfilled expectations");
        }

        /// <summary>
        /// Clears any pending requests configured with Expects
        /// </summary>
        public void ResetExpectations()
        {
            this.requestExpectations.Clear();
        }
    }
}