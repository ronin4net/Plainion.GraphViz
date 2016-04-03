﻿using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Mvvm;
using Plainion.Collections;
using Plainion.GraphViz.Algorithms;
using Plainion.GraphViz.Infrastructure.ViewModel;
using Plainion.GraphViz.Presentation;
using Plainion.Prism.Mvvm;
using Plainion.Windows.Controls.Tree;

namespace Plainion.GraphViz.Modules.Analysis
{
    [Export( typeof( ClusterEditorModel ) )]
    class ClusterEditorModel : ViewModelBase
    {
        private string myFilter;
        private bool myFilterOnId;
        private ICollectionView myPreviewNodes;
        private NodeWithCaption mySelectedPreviewItem;
        private IGraphPresentation myPresentation;
        private DragDropBehavior myDragDropBehavior;
        private string mySelectedCluster;
        private string myAddButtonCaption;
        private IModuleChangedObserver myTransformationsObserver;

        [ImportingConstructor]
        public ClusterEditorModel()
        {
            AddButtonCaption = "Add ...";
            AddCommand = new DelegateCommand( OnAdd, () => SelectedCluster != null );
            MouseDownCommand = new DelegateCommand<MouseButtonEventArgs>( OnMouseDown );

            Root = new ClusterTreeNode();

            myDragDropBehavior = new DragDropBehavior( Root );
            DropCommand = new DelegateCommand<NodeDropRequest>( myDragDropBehavior.ApplyDrop );
        }

        public ClusterTreeNode Root { get; private set; }

        public ICommand DropCommand { get; private set; }

        public ICommand MouseDownCommand { get; private set; }

        private void OnMouseDown( MouseButtonEventArgs args )
        {
            if( args.ClickCount == 2 )
            {
                Filter = SelectedPreviewItem.DisplayText;
            }
        }

        public DelegateCommand AddCommand { get; private set; }

        private void OnAdd()
        {
            var operation = new ChangeClusterAssignment( myPresentation );

            foreach( var node in PreviewNodes.Cast<NodeWithCaption>().ToList() )
            {
                operation.Execute( t => t.AddToCluster( node.Node.Id, SelectedCluster ) );
            }

            Filter = null;
        }

        public string SelectedCluster
        {
            get { return mySelectedCluster; }
            set
            {
                if( SetProperty( ref mySelectedCluster, value ) )
                {
                    AddButtonCaption = SelectedCluster != null ? "Add to '" + mySelectedCluster + "'" : "Add ...";
                    AddCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string AddButtonCaption
        {
            get { return myAddButtonCaption; }
            set { SetProperty( ref myAddButtonCaption, value ); }
        }

        public NodeWithCaption SelectedPreviewItem
        {
            get { return mySelectedPreviewItem; }
            set { SetProperty( ref mySelectedPreviewItem, value ); }
        }

        protected override void OnModelPropertyChanged( string propertyName )
        {
            if( propertyName == "Presentation" )
            {
                if( myPresentation == Model.Presentation )
                {
                    return;
                }

                if( myTransformationsObserver != null )
                {
                    myTransformationsObserver.ModuleChanged -= OnTransformationsChanged;
                    myTransformationsObserver.Dispose();
                }

                myPresentation = Model.Presentation;

                Filter = null;

                myPreviewNodes = null;
                PreviewNodes.Refresh();

                BuildTree();

                // register for updates only AFTER tree is built up completely to avoid getting notified by the built up process
                var transformationModule = Model.Presentation.GetModule<ITransformationModule>();
                myTransformationsObserver = transformationModule.CreateObserver();
                myTransformationsObserver.ModuleChanged += OnTransformationsChanged;
            }
        }

        private void BuildTree()
        {
            Root.Children.Clear();

            SelectedCluster = null;

            var transformationModule = myPresentation.GetModule<ITransformationModule>();
            var captionModule = myPresentation.GetModule<ICaptionModule>();
            foreach( var cluster in transformationModule.Graph.Clusters )
            {
                var clusterNode = new ClusterTreeNode
                {
                    Parent = Root,
                    Id = cluster.Id,
                    Caption = captionModule.Get( cluster.Id ).DisplayText,
                    IsExpanded = true
                };
                Root.Children.Add( clusterNode );

                clusterNode.Children.AddRange( cluster.Nodes
                    .Select( n => new ClusterTreeNode
                    {
                        Parent = clusterNode,
                        Id = n.Id,
                        Caption = captionModule.Get( n.Id ).DisplayText
                    } ) );
            }

            // register for notifications after tree is built to avoid intermediate states getting notified

            foreach( ClusterTreeNode cluster in Root.Children )
            {
                PropertyChangedEventManager.AddHandler( cluster, OnSelectionChanged, PropertySupport.ExtractPropertyName( () => cluster.IsSelected ) );

                foreach( ClusterTreeNode node in cluster.Children )
                {
                    PropertyChangedEventManager.AddHandler( node, OnSelectionChanged, PropertySupport.ExtractPropertyName( () => node.IsSelected ) );
                    PropertyChangedEventManager.AddHandler( node, OnParentChanged, PropertySupport.ExtractPropertyName( () => node.Parent ) );
                }
            }
        }

        private void OnParentChanged( object sender, PropertyChangedEventArgs e )
        {
            var node = ( ClusterTreeNode )sender;

            new ChangeClusterAssignment( myPresentation )
                .Execute( t => t.AddToCluster( node.Id, ( ( ClusterTreeNode )node.Parent ).Id ) );
        }
        private void OnSelectionChanged( object sender, PropertyChangedEventArgs e )
        {
            var selectedCluster = Root.Children.FirstOrDefault( n => n.IsSelected );
            if( selectedCluster == null )
            {
                var selectedNode = Root.Children
                    .SelectMany( n => n.Children )
                    .FirstOrDefault( n => n.IsSelected );

                if( selectedNode != null )
                {
                    selectedCluster = ( ClusterTreeNode )selectedNode.Parent;
                }
            }

            if( selectedCluster == null )
            {
                SelectedCluster = null;
            }
            else
            {
                SelectedCluster = selectedCluster.Id;
            }
        }

        private void OnTransformationsChanged( object sender, EventArgs e )
        {
            BuildTree();
            PreviewNodes.Refresh();
        }

        public string Filter
        {
            get { return myFilter; }
            set
            {
                if( SetProperty( ref myFilter, value ) )
                {
                    ClearErrors();
                    PreviewNodes.Refresh();
                }
            }
        }

        public bool FilterOnId
        {
            get { return myFilterOnId; }
            set
            {
                if( SetProperty( ref myFilterOnId, value ) )
                {
                    ClearErrors();
                    PreviewNodes.Refresh();
                }
            }
        }

        public ICollectionView PreviewNodes
        {
            get
            {
                if( myPreviewNodes == null && myPresentation != null )
                {
                    var captionModule = myPresentation.GetPropertySetFor<Caption>();

                    var nodes = myPresentation.Graph.Nodes
                        .Select( n => new NodeWithCaption( n, myFilterOnId ? n.Id : captionModule.Get( n.Id ).DisplayText ) );

                    myPreviewNodes = CollectionViewSource.GetDefaultView( nodes );
                    myPreviewNodes.Filter = FilterPreview;
                    myPreviewNodes.SortDescriptions.Add( new SortDescription( "DisplayText", ListSortDirection.Ascending ) );

                    OnPropertyChanged( "PreviewNodes" );
                }
                return myPreviewNodes;
            }
        }

        private bool FilterPreview( object item )
        {
            if( GetErrors( "Filters" ).OfType<object>().Any() )
            {
                return true;
            }

            var node = ( NodeWithCaption )item;

            var transformationModule = myPresentation.GetModule<ITransformationModule>();
            if( transformationModule.Graph.Clusters.Any( c => c.Nodes.Any( n => n.Id == node.Node.Id ) ) )
            {
                return false;
            }

            if( string.IsNullOrEmpty( Filter ) )
            {
                return true;
            }

            var pattern = Filter;

            if( !pattern.Contains( '*' ) )
            {
                pattern = "*" + pattern + "*";
            }

            try
            {
                return new Plainion.Text.Wildcard( pattern, RegexOptions.IgnoreCase ).IsMatch( node.DisplayText );
            }
            catch
            {
                SetError( ValidationFailure.Error, "Filter" );
                return true;
            }
        }
    }
}
