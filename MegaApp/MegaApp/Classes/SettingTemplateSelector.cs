﻿using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml;
using MegaApp.Interfaces;
using MegaApp.ViewModels.Settings;

namespace MegaApp.Classes
{
    public class SettingTemplateSelector : DataTemplateSelector
    {
        public DataTemplate AppVersionInfoItemTemplate { get; set; }
        public DataTemplate BooleanItemTemplate { get; set; }
        public DataTemplate DescriptionItemTemplate { get; set; }
        public DataTemplate ActionItemTemplate { get; set; }
        public DataTemplate RecoveryKeyItemTemplate { get; set; }
        public DataTemplate InformationItemTemplate { get; set; }
        public DataTemplate AcknowledgementsItemTemplate { get; set; }
        public DataTemplate InfoActionItemTemplate { get; set; }
        public DataTemplate InfoButtonItemTemplate { get; set; }
        public DataTemplate LegalAndPoliciesItemTemplate { get; set; }
        public DataTemplate SelectionItemTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is AppVersionSettingViewModel) return this.AppVersionInfoItemTemplate;
            if (item is DescriptionSettingViewModel) return this.DescriptionItemTemplate;
            if (item is RecoveryKeySettingViewModel) return this.RecoveryKeyItemTemplate;
            if (item is SdkVersionSettingViewModel) return this.InfoActionItemTemplate;
            if (item is AcknowledgementsSettingViewModel) return this.AcknowledgementsItemTemplate;
            if (item is LinkSettingViewModel) return this.InfoActionItemTemplate;
            if (item is ButtonSettingViewModel) return this.InfoButtonItemTemplate;
            if (item is LegalAndPoliciesSettingViewModel) return this.LegalAndPoliciesItemTemplate;
            if (item is SelectionSettingViewModel) return this.SelectionItemTemplate;

            var setting = item as ISetting;

            if (setting?.Value is bool) return this.BooleanItemTemplate;

            return this.ActionItemTemplate;
;        }
        
    }
}
