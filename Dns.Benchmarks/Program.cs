// //-------------------------------------------------------------------------------------------------
// // <copyright file="Program.cs" company="stephbu">
// // Copyright (c) Steve Butler. All rights reserved.
// // </copyright>
// //-------------------------------------------------------------------------------------------------

using BenchmarkDotNet.Running;
using Dns.Benchmarks;

// Run all benchmarks
BenchmarkSwitcher.FromAssembly(typeof(DnsProtocolBenchmarks).Assembly).Run(args);