﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.UI.Xaml.Controls;
using MegaApp.Interfaces;
using MegaApp.UserControls;
using INavigate = MegaApp.Interfaces.INavigate;

namespace MegaApp.Services
{
    /// <summary>
    /// Service class used to navigate between views in the app
    /// </summary>
    public class NavigateService: INavigate
    {
        private static NavigateService _instance;
        // Singleton instance
        public static NavigateService Instance => _instance ?? (_instance = new NavigateService());
        
        // App rootFrame to navigate 
        public static Frame MainFrame { get; set; }

        public static Frame CoreFrame { get; set; }

        public static IList<Type> PageExTypes { get; set; }

        public static IList<Type> TypeList { get; set; }

        private static Frame GetFrame(bool useCoreFrame)
        {
            return useCoreFrame ? CoreFrame : MainFrame;
        }

        /// <summary>
        /// NavigateTo to the specified view
        /// </summary>
        /// <param name="viewType">Type of the view to navigate</param>
        /// <param name="useCoreFrame">Use the app core rootframe or use the app hamburger mainframe</param>
        /// <param name="navObj">Optional object with navigation parameters and information</param>
        /// <returns>True if navigation succeeded, else False</returns>
        public bool Navigate(Type viewType, bool useCoreFrame = false, INavigationObject navObj = null)
        {
            return GetFrame(useCoreFrame).Navigate(viewType, navObj);
        }

        /// <summary>
        /// NavigateTo backwards in backstack if possible
        /// </summary>
        /// <param name="useCoreFrame">Use the app core rootframe or use the app hamburger mainframe</param>
        public void GoBack(bool useCoreFrame = false)
        {
            var navigateFrame = GetFrame(useCoreFrame);
            if(navigateFrame.CanGoBack) navigateFrame.GoBack();
        }

        /// <summary>
        /// NavigateTo forwards in forward stack if possible
        /// </summary>
        /// <param name="useCoreFrame">Use the app core rootframe or use the app hamburger mainframe</param>
        public void GoForward(bool useCoreFrame = false)
        {
            var navigateFrame = GetFrame(useCoreFrame);
            if (navigateFrame.CanGoForward) navigateFrame.GoForward();
        }

        /// <summary>
        /// Cast object to INavigationObject
        /// </summary>
        /// <param name="obj">Object to cast</param>
        /// <returns>A navigation object interface or NULL if cast is invalid</returns>
        public static INavigationObject GetNavigationObject(object obj)
        {
            return obj as INavigationObject;
        }

        /// <summary>
        /// Get the view which implements the BasePageViewModel by reflection
        /// </summary>
        /// <param name="viewModelType">The viewmodel type that must be implemented</param>
        /// <returns>Type of the view that implements the specified viewmodel</returns>
        public static Type GetViewType(Type viewModelType)
        {
            if (TypeList == null)
            {
                var assembly = viewModelType.GetTypeInfo().Assembly;
                // Store types in static property for faster access 
                TypeList = assembly.GetTypes().ToList();
            }
            
            if (PageExTypes == null)
            {
                // Store page types in static property for faster access 
                PageExTypes = TypeList.Where(t => t.GetTypeInfo().BaseType != null &&
                    t.GetTypeInfo().BaseType.GetTypeInfo().IsGenericType &&
                    t.GetTypeInfo().BaseType.GetGenericTypeDefinition() == typeof(PageEx<>)).ToList();
            }

            var baseViewType = PageExTypes.First(t => t.GetTypeInfo().BaseType != null &&
                    t.GetTypeInfo().BaseType.GenericTypeArguments[0] == viewModelType);

            var viewType = TypeList.FirstOrDefault(t => t.GetTypeInfo().BaseType != null &&
                    t.GetTypeInfo().BaseType == baseViewType);

            return viewType;
        }
    }
}
