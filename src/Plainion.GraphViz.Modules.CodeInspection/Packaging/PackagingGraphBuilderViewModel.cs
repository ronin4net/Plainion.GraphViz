﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Xml;
using Akka.Actor;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Interactivity.InteractionRequest;
using Plainion.GraphViz.Infrastructure.Services;
using Plainion.GraphViz.Infrastructure.ViewModel;
using Plainion.GraphViz.Model;
using Plainion.GraphViz.Modules.CodeInspection.Controls;
using Plainion.GraphViz.Modules.CodeInspection.Packaging.Services;
using Plainion.GraphViz.Modules.CodeInspection.Packaging.Spec;
using Plainion.GraphViz.Presentation;
using Plainion.Prism.Interactivity.InteractionRequest;

namespace Plainion.GraphViz.Modules.CodeInspection.Packaging
{
    [Export(typeof(PackagingGraphBuilderViewModel))]
    class PackagingGraphBuilderViewModel : ViewModelBase
    {
        private int myProgress;
        private bool myIsReady;
        private string myPackageName;
        private AnalysisMode myAnalysisMode;
        private TextDocument myDocument;
        private IEnumerable<KeywordCompletionData> myCompletionData;
        private CancellationTokenSource myCTS;

        public PackagingGraphBuilderViewModel()
        {
            Document = new TextDocument();
            Document.Changed += Document_Changed;

            CreateGraphCommand = new DelegateCommand(OnCreateGraph, () => IsReady && IsValidDocument());
            CancelCommand = new DelegateCommand(OnCancel, () => !IsReady);

            ClosedCommand = new DelegateCommand(OnClosed);

            OpenCommand = new DelegateCommand(OnOpen, () => IsReady);
            OpenFileRequest = new InteractionRequest<OpenFileDialogNotification>();

            myCompletionData = GetType().Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(SystemPackaging).Namespace)
                .Where(t => !t.IsAbstract)
                .Where(t => t.GetCustomAttribute(typeof(CompilerGeneratedAttribute), true) == null)
                .Select(t => new KeywordCompletionData(t))
                .ToList();

            AnalysisMode = AnalysisMode.CrossPackageDependencies;
            IsReady = true;
        }

        private bool IsValidDocument()
        {
            if (string.IsNullOrEmpty(Document.Text))
            {
                return false;
            }

            try
            {
                using (var reader = new StringReader(Document.Text))
                {
                    XamlReader.Load(XmlReader.Create(reader));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Document_Changed(object sender, DocumentChangeEventArgs e)
        {
            CreateGraphCommand.RaiseCanExecuteChanged();
        }

        public TextDocument Document
        {
            get { return myDocument; }
            set { SetProperty(ref myDocument, value); }
        }

        public IEnumerable<KeywordCompletionData> CompletionData
        {
            get { return myCompletionData; }
            set { SetProperty(ref myCompletionData, value); }
        }

        public AnalysisMode AnalysisMode
        {
            get { return myAnalysisMode; }
            set { SetProperty(ref myAnalysisMode, value); }
        }

        public string PackageName
        {
            get { return myPackageName; }
            set { SetProperty(ref myPackageName, value); }
        }

        public bool IsReady
        {
            get { return myIsReady; }
            set
            {
                if (SetProperty(ref myIsReady, value))
                {
                    CreateGraphCommand.RaiseCanExecuteChanged();
                    CancelCommand.RaiseCanExecuteChanged();
                    OpenCommand.RaiseCanExecuteChanged();
                }
            }
        }

        [Import]
        public IPresentationCreationService PresentationCreationService { get; set; }

        [Import]
        public IStatusMessageService StatusMessageService { get; set; }

        [Import]
        public PackageAnalysisService AnalysisService { get; set; }

        public DelegateCommand CreateGraphCommand { get; private set; }

        public DelegateCommand CancelCommand { get; private set; }

        public DelegateCommand OpenCommand { get; private set; }

        public DelegateCommand ClosedCommand { get; private set; }

        public InteractionRequest<OpenFileDialogNotification> OpenFileRequest { get; private set; }

        private void OnOpen()
        {
            var notification = new OpenFileDialogNotification();
            notification.RestoreDirectory = true;
            notification.Filter = "Packaging Spec (*.xaml)|*.xaml";
            notification.FilterIndex = 0;
            notification.CheckFileExists = false;

            OpenFileRequest.Raise(notification,
                n =>
                {
                    if (n.Confirmed)
                    {
                        if (File.Exists(n.FileName))
                        {
                            Document.Text = File.ReadAllText(n.FileName);
                        }
                        else
                        {
                            using (var stream = GetType().Assembly.GetManifestResourceStream("Plainion.GraphViz.Modules.CodeInspection.Resources.SystemPackagingTemplate.xaml"))
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Document.Text = reader.ReadToEnd();
                                }
                            }
                        }
                        Document.FileName = n.FileName;
                    }
                });
        }

        private async void OnCreateGraph()
        {
            IsReady = false;

            Save();

            var request = new AnalysisRequest
            {
                Spec = Document.Text,
                AnalysisMode = AnalysisMode,
                PackageName = PackageName,
                OutputFile = Path.GetTempFileName()
            };

            try
            {
                myCTS = new CancellationTokenSource();

                var doc = await AnalysisService.Analyse(request, myCTS.Token);

                myCTS = null;

                if (doc != null)
                {
                    BuildGraph(doc);
                }
            }
            finally
            {
                IsReady = true;
            }
        }

        private void BuildGraph(AnalysisDocument response)
        {
            if (!response.Nodes.Any() && !response.Edges.Any())
            {
                MessageBox.Show("Neither nodes nor edges found");
                return;
            }

            var presentation = PresentationCreationService.CreatePresentation(Path.GetTempPath());

            var builder = new RelaxedGraphBuilder();
            foreach (var edge in response.Edges)
            {
                builder.TryAddEdge(edge.Item1, edge.Item2);
            }
            foreach (var node in response.Nodes)
            {
                builder.TryAddNode(node);
            }
            foreach (var cluster in response.Clusters)
            {
                builder.TryAddCluster(cluster.Key, cluster.Value);
            }

            presentation.Graph = builder.Graph;

            var captionModule = presentation.GetPropertySetFor<Caption>();
            foreach (var caption in response.Captions)
            {
                captionModule.Add(caption);
            }

            var nodeStyles = presentation.GetPropertySetFor<NodeStyle>();
            foreach (var style in response.NodeStyles)
            {
                nodeStyles.Add(style);
            }

            Model.Presentation = presentation;
        }

        private void OnCancel()
        {
            if (myCTS != null)
            {
                myCTS.Cancel();
            }

            IsReady = true;
        }

        internal void OnClosed()
        {
            Save();
            Document.Text = string.Empty;

            if (myCTS != null)
            {
                myCTS.Cancel();
            }

            IsReady = true;
        }

        private void Save()
        {
            if (Document.FileName != null)
            {
                File.WriteAllText(Document.FileName, Document.Text);
            }
        }

        protected override void OnModelPropertyChanged(string propertyName)
        {
        }

        public int ProgressValue
        {
            get { return myProgress; }
            set { SetProperty(ref myProgress, value); }
        }
    }
}