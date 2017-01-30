﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DtoGenerator.Logic.Infrastructure;
using DtoGenerator.Logic.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DtoGenerator.Logic.UI
{
    public class PropertySelectorViewModel : ViewModelBase
    {
        public static async Task<PropertySelectorViewModel> Create(Document doc, string dtoName, SolutionLocation dtoLocation, Document existingDto = null)
        {
            var autogeneratedProperties = await EntityParser.GetAutoGeneratedProperties(existingDto);

            var instance = new PropertySelectorViewModel();
            instance.EntityModel = await EntityViewModel.CreateRecursive(doc, depth: 3, existingProperties: autogeneratedProperties, canReuseBaseMapper: true);
            instance.EntityModel.DtoName = dtoName;
            instance.DtoLocation = dtoLocation;

            var isDerived = await EntityParser.HasBaseDto(existingDto, instance.EntityModel.BaseEntityDtoName);
            instance.EntityModel.ReuseBaseEntityMapper |= isDerived;

            instance.AddDataContract = await EntityParser.HasDataContract(existingDto);

            return instance;
        }

        private PropertySelectorViewModel()
        {
            this.GenerateMapper = true;
        }

        private bool _generateMapper;
        public bool GenerateMapper
        {
            get
            {
                return this._generateMapper;
            }
            set
            {
                if (value != this._generateMapper)
                {
                    this._generateMapper = value;
                    this.InvokePropertyChanged(nameof(GenerateMapper));
                }
            }
        }

        private bool _addDataContract;
        public bool AddDataContract
        {
            get
            {
                return this._addDataContract;
            }
            set
            {
                if (value != this._addDataContract)
                {
                    this._addDataContract = value;
                    this.InvokePropertyChanged(nameof(AddDataContract));
                }
            }
        }

        public SolutionLocation DtoLocation { get; set; }

        public string DtoLocationStr => DtoLocation.ToString();

        private EntityViewModel _entityModel;
        public EntityViewModel EntityModel
        {
            get
            {
                return this._entityModel;
            }
            set
            {
                if (value != this._entityModel)
                {
                    this._entityModel = value;
                    this.InvokePropertyChanged(nameof(EntityModel));
                }
            }
        }

        public EntityMetadata GetMetadata()
        {
            if (this._entityModel != null)
                return this._entityModel.ConvertToMetadata();

            return null;
        }
    }

    public class EntityViewModel : ViewModelBase
    {
        private EntityMetadata _originalMetadata;

        public string EntityName { get; set; }
        public string DtoName { get; set; }

        private bool _canReuseBaseMapper;
        public bool CanReuseBaseMapper
        {
            get
            {
                return this._canReuseBaseMapper;
            }
            set
            {
                if (value != this._canReuseBaseMapper)
                {
                    this._canReuseBaseMapper = value;
                    this.InvokePropertyChanged(nameof(CanReuseBaseMapper));
                }
            }
        }


        private bool _reuseBaseEntityMapper;
        public bool ReuseBaseEntityMapper
        {
            get
            {
                return this._reuseBaseEntityMapper;
            }
            set
            {
                if (value != this._reuseBaseEntityMapper)
                {
                    this._reuseBaseEntityMapper = value;
                    this.InvokePropertyChanged(nameof(ReuseBaseEntityMapper));

                    foreach (var prop in this.Properties.Where(p => p.IsInherited))
                        prop.IsVisible = !value;
                }
            }
        }

        private string _baseEntityDtoName;
        public string BaseEntityDtoName
        {
            get
            {
                return this._baseEntityDtoName;
            }
            set
            {
                if (value != this._baseEntityDtoName)
                {
                    this._baseEntityDtoName = value;
                    this.InvokePropertyChanged(nameof(BaseEntityDtoName));
                }
            }
        }

        public ObservableCollection<PropertyViewModel> Properties { get; set; }

        public static async Task<EntityViewModel> CreateRecursive(Document doc, int depth = 3, bool autoSelect = true, bool canSelectCollections = true, List<string> existingProperties = null, bool canReuseBaseMapper = false)
        {
            var instance = new EntityViewModel();

            instance.Properties = new ObservableCollection<PropertyViewModel>();

            instance._originalMetadata = await EntityParser.FromDocument(doc, includeInherited: true);
            instance.EntityName = instance._originalMetadata.Name;

            foreach (var p in instance._originalMetadata.Properties)
            {
                var propViewModel = new PropertyViewModel(instance);
                propViewModel.Name = p.Name;
                propViewModel.IsInherited = p.IsInherited;
                propViewModel.IsVisible = true;
                propViewModel.Type = p.Type;
                propViewModel.CanSelect = true;

                if (p.IsCollection && !canSelectCollections)
                    propViewModel.CanSelect = false;

                propViewModel.IsSelected = autoSelect && p.IsSimpleProperty;

                if(existingProperties != null)
                    propViewModel.IsSelected = propViewModel.CanSelect && existingProperties.Any(x => x == p.Name);

                if (p.IsRelation && !p.IsCollection && depth > 0)
                {
                    var relatedDoc = await doc.GetRelatedEntityDocument(p.RelatedEntityName);
                    if(relatedDoc != null)
                    {
                        var relatedProperties = existingProperties == null 
                            ? null 
                            : existingProperties.Where(x => x.StartsWith(p.Name))
                                .Where(x => !instance._originalMetadata.Properties.Any(o => o.Name == x))
                                .Select(x => x.Substring(p.Name.Length))
                                .ToList();

                        propViewModel.RelatedEntity = await CreateRecursive(relatedDoc, depth: depth - 1, autoSelect: false, canSelectCollections: false, existingProperties: relatedProperties);
                    }
                    else
                    {
                        p.IsRelation = false;
                        p.IsSimpleProperty = true;
                    }
                }

                instance.Properties.Add(propViewModel);
            }

            if (canReuseBaseMapper && !string.IsNullOrWhiteSpace(instance._originalMetadata.BaseClassDtoName))
            {
                instance.CanReuseBaseMapper = true;
                instance.BaseEntityDtoName = instance._originalMetadata.BaseClassDtoName;

                if (existingProperties == null)
                    instance.ReuseBaseEntityMapper = true;
            }

            return instance;
        }

        private EntityViewModel()
        {

        }

        public EntityMetadata ConvertToMetadata()
        {
            var result = this._originalMetadata.Clone();
            result.DtoName = this.DtoName;

            if(this.ReuseBaseEntityMapper)
                result.BaseClassDtoName = this.BaseEntityDtoName;

            var selectedProperties = this.Properties
                .Where(p => p.IsSelected)
                .ToList();

            var relatedPropertiesWithSelection = this.Properties
                .Where(p => p.RelatedEntity != null)
                .Where(p => p.RelatedEntity.HasSelectionInSubtree())
                .ToList();

            var toRemoveFromMetadata = new List<Model.PropertyMetadata>();
            foreach(var x in result.Properties)
            {
                if (!selectedProperties.Concat(relatedPropertiesWithSelection).Any(p => p.Name == x.Name))
                {
                    toRemoveFromMetadata.Add(x);
                }
                else if(x.IsRelation && !x.IsCollection)
                {
                    var related = relatedPropertiesWithSelection
                        .Where(p => p.Name == x.Name)
                        .Select(p => p.RelatedEntity)
                        .FirstOrDefault();

                    x.RelationMetadata = related?.ConvertToMetadata();
                }
            }

            result.Properties.RemoveAll(p => toRemoveFromMetadata.Contains(p));

            return result;
        }

        public bool HasSelectionInSubtree()
        {
            return this.Properties.Any(p => p.IsSelected) || 
                this.Properties
                    .Where(p => p.RelatedEntity != null)
                    .Any(p => p.RelatedEntity.HasSelectionInSubtree());
        }
    }

    public class PropertyViewModel : ViewModelBase
    {
        private EntityViewModel _entityViewModel;

        public PropertyViewModel(EntityViewModel entityModel)
        {
            this._entityViewModel = entityModel;
        }

        public string Type { get; set; }
        public string Name { get; set; }
        public bool IsInherited { get; set; }

        public string NameFormatted => $"{Name} ({Type})";

        public Brush TextColor
        {
            get
            {
                if (!this.CanSelect)
                    return new SolidColorBrush(Colors.LightGray);

                return IsInherited ? new SolidColorBrush(Colors.DimGray) : new SolidColorBrush(Colors.Black);
            }
        }

        public bool IsEnabled
        {
            get
            {
                return this.CanSelect && this.IsVisible;
            }
        }


        private bool _isVisible;
        public bool IsVisible
        {
            get
            {
                return this._isVisible;
            }
            set
            {
                if (value != this._isVisible)
                {
                    this._isVisible = value;
                    this.InvokePropertyChanged(nameof(IsVisible));
                    this.InvokePropertyChanged(nameof(IsEnabled));
                    this.InvokePropertyChanged(nameof(TextColor));

                    if (value == false)
                        this.IsSelected = false;
                }
            }
        }


        private bool _isSelected;
        public bool IsSelected
        {
            get
            {
                return this._isSelected;
            }
            set
            {
                if (value != this._isSelected)
                {
                    this._isSelected = value;
                    this.InvokePropertyChanged(nameof(IsSelected));

                    if(this._relatedEntity != null)
                    {
                        foreach (var prop in this._relatedEntity.Properties)
                            prop.IsSelected = value;
                    }
                }
            }
        }

        private bool _canSelect;
        public bool CanSelect
        {
            get
            {
                return this._canSelect;
            }
            set
            {
                if (value != this._canSelect)
                {
                    this._canSelect = value;
                    this.InvokePropertyChanged(nameof(CanSelect));
                    this.InvokePropertyChanged(nameof(IsEnabled));
                    this.InvokePropertyChanged(nameof(TextColor));
                }
            }
        }

        private EntityViewModel _relatedEntity;
        public EntityViewModel RelatedEntity
        {
            get
            {
                return this._relatedEntity;
            }
            set
            {
                if (value != this._relatedEntity)
                {
                    this._relatedEntity = value;
                    this.InvokePropertyChanged(nameof(RelatedEntity));
                }
            }
        }
    }
}
