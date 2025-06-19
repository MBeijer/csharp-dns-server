// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="HttpServer.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dns.Contracts;

namespace Dns;

internal delegate void OnHttpRequestHandler(HttpListenerContext context);
internal delegate void OnHandledException(Exception ex);

/// <summary>HTTP data receiver</summary>
internal class HttpServer : IHtmlDump
{
    private HttpListener _listener;
    private bool         _running;

    private int _requestCounter;
    private int _request200;
    private int _request300;
    private int _request400;
    private int _request500;
    private int _request600;

    private readonly string _machineName = Environment.MachineName;

    public event OnHttpRequestHandler OnProcessRequest;
    public event OnHttpRequestHandler OnHealthProbe;
    public event OnHandledException   OnHandledException;

    /// <summary>Configure listener</summary>
    public void Initialize(params string[] prefixes)
    {
        _listener = new();
        foreach (var prefix in prefixes)
        {
            _listener.Prefixes.Add(prefix);
        }
    }

    /// <summary>Start listening</summary>
    public async void Start(CancellationToken ct)
    {
        ct.Register(Stop);
        _listener.Start();
        while (true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var processRequest = Task.Run(() => ProcessRequest(context), ct);
            }
            catch (HttpListenerException ex)
            {
                OnHandledException?.Invoke(ex);
                break;
            }
            catch (InvalidOperationException ex)
            {
                OnHandledException?.Invoke(ex);
                break;
            }
        }
    }

    /// <summary>Stop listening</summary>
    private void Stop()
    {
        if (_running)
        {
            _running = false;
        }

        _listener.Stop();
    }

    /// <summary>Process incoming request</summary>
    private void ProcessRequest(HttpListenerContext context)
    {

        // log
        // performance counters
        try
        {
            // special case health probes
            if (context.Request.RawUrl != null && context.Request.RawUrl.Equals("/health/keepalive", StringComparison.InvariantCultureIgnoreCase))
            {
                HealthProbe(context);
            }
            else
            {
                OnProcessRequest?.Invoke(context);
            }
        }
        catch (Exception ex)
        {
            // TODO: log exception
            OnHandledException?.Invoke(ex);
            //context.Response.StatusCode = 500;
        }

        //context.Response.OutputStream.Dispose();

        var statusCode = context.Response.StatusCode;

        switch (statusCode)
        {
            case >= 200 and < 300:
                _request200++;
                break;
            case >= 300 and < 400:
                _request300++;
                break;
            case >= 400 and < 500:
                _request400++;
                break;
            case >= 500 and < 600:
                _request500++;
                break;
            case >= 600 and < 700:
                _request600++;
                break;
        }

        _requestCounter++;
    }

    /// <summary>Process health probe request</summary>
    private void HealthProbe(HttpListenerContext context)
    {

        if (OnHealthProbe != null)
        {
            OnHealthProbe(context);
        }
        else
        {
            context.Response.StatusCode = 200;
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentType = "text/html";
            using var writer = context.Response.OutputStream.CreateWriter();
            DumpHtml(writer);
        }
    }

    public void DumpHtml(TextWriter writer)
    {
        writer.WriteLine("Health Probe<br/>");
        writer.WriteLine("Machine: {0}<br/>", _machineName);
        writer.WriteLine("Count: {0}<br/>", _requestCounter);
        writer.WriteLine("200: {0}<br/>", _request200);
        writer.WriteLine("300: {0}<br/>", _request300);
        writer.WriteLine("400: {0}<br/>", _request400);
        writer.WriteLine("500: {0}<br/>", _request500);
        writer.WriteLine("600: {0}<br/>", _request600);
    }
}