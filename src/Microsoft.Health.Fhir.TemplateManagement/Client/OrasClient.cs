﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.TemplateManagement.Client
{
    // Todo need to be async

    public class OrasClient : IOrasClient
    {
        private readonly string _imageReference;

        public OrasClient(string imageReference)
        {
            EnsureArg.IsNotNull(imageReference, nameof(imageReference));

            _imageReference = imageReference;
        }

        public async Task<bool> PullImageAsync(string outputFolder)
        {
            string command = $"pull  {_imageReference} -o {outputFolder}";
            await OrasExecutionAsync(command, ".");
            return true;
        }

        public async Task<bool> PushImageAsync(string inputFolder)
        {
            string argument = string.Empty;
            string command = $"push {_imageReference}";

            if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"No file for push.");
                return false;
            }

            var filePathToPush = Directory.EnumerateFiles(inputFolder, "*.tar.gz", SearchOption.AllDirectories);
            foreach (var filePath in filePathToPush)
            {
                argument += $" {Path.GetRelativePath(inputFolder, filePath)}";
            }

            if (string.IsNullOrEmpty(argument))
            {
                Console.WriteLine($"No file for push.");
                return false;
            }

            // In order to remove image's directory prefix. (e.g. "layers/layer1.tar.gz" --> "layer1.tar.gz"
            // Change oras working folder into imageLayerFolder
            await OrasExecutionAsync(string.Concat(command, argument), inputFolder);
            return true;
        }

        private async Task OrasExecutionAsync(string command, string orasWorkingDirectory)
        {
            TaskCompletionSource<bool> eventHandled = new TaskCompletionSource<bool>();
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "oras.exe")),
            };

            process.StartInfo.Arguments = command;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WorkingDirectory = orasWorkingDirectory;
            process.EnableRaisingEvents = true;

            // Add event to make it async.
            process.Exited += new EventHandler((sender, e) => { eventHandled.TrySetResult(true); });
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new OrasException(TemplateManagementErrorCode.OrasProcessFailed, "Oras process failed", ex);
            }

            StreamReader errStreamReader = process.StandardError;
            await Task.WhenAny(eventHandled.Task, Task.Delay(30000));
            if (process.HasExited)
            {
                string error = errStreamReader.ReadToEnd();
                if (!string.IsNullOrEmpty(error))
                {
                    throw new OrasException(TemplateManagementErrorCode.OrasProcessFailed, error);
                }
            }
            else
            {
                throw new OrasException(TemplateManagementErrorCode.OrasTimeOut, "Oras request timeout");
            }
        }

    }
}