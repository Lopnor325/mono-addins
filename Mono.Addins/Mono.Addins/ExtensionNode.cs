//
// ExtensionNode.cs
//
// Author:
//   Lluis Sanchez Gual
//
// Copyright (C) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Reflection;
using Mono.Addins.Description;

namespace Mono.Addins
{
	public class ExtensionNode
	{
		bool childrenLoaded;
		TreeNode treeNode;
		ExtensionNodeList childNodes;
		RuntimeAddin addin;
		string addinId;
		ExtensionNodeType nodeType;
		ModuleDescription module;
		event ExtensionNodeEventHandler extensionNodeChanged;
		
		public string Id {
			get { return treeNode != null ? treeNode.Id : string.Empty; }
		}
		
		public string Path {
			get { return treeNode != null ? treeNode.GetPath () : string.Empty; }
		}
		
		public ExtensionNode Parent {
			get {
				if (treeNode != null && treeNode.Parent != null)
					return treeNode.Parent.ExtensionNode;
				else
					return null;
			}
		}
		
		public ExtensionContext ExtensionContext {
			get { return treeNode.Context; }
		}
		
		public bool HasId {
			get { return !Id.StartsWith (ExtensionTree.AutoIdPrefix); }
		}
		
		internal void SetTreeNode (TreeNode node)
		{
			treeNode = node;
		}
		
		internal void SetData (string plugid, ExtensionNodeType nodeType, ModuleDescription module)
		{
			this.addinId = plugid;
			this.nodeType = nodeType;
			this.module = module;
		}
		
		internal string AddinId {
			get { return addinId; }
		}
		
		internal TreeNode TreeNode {
			get { return treeNode; }
		}
		
		public RuntimeAddin Addin {
			get {
				if (addin == null && addinId != null) {
					if (!AddinManager.SessionService.IsAddinLoaded (addinId))
						AddinManager.SessionService.LoadAddin (null, addinId, true);
					addin = AddinManager.SessionService.GetAddin (addinId);
					if (addin != null)
						addin = addin.GetModule (module);
				}
				if (addin == null)
					throw new InvalidOperationException ("Add-in '" + addinId + "' could not be loaded.");
				return addin; 
			}
		}
		
		public event ExtensionNodeEventHandler ExtensionNodeChanged {
			add {
				extensionNodeChanged += value;
				foreach (ExtensionNode node in ChildNodes) {
					try {
						value (this, new ExtensionNodeEventArgs (ExtensionChange.Add, node));
					} catch (Exception ex) {
						AddinManager.ReportError (null, node.Addin != null ? node.Addin.Id : null, ex, false);
					}
				}
			}
			remove {
				extensionNodeChanged -= value;
			}
		}
		
		public ExtensionNodeList ChildNodes {
			get {
				if (childrenLoaded)
					return childNodes;
				
				try {
					if (treeNode.Children.Count == 0) {
						childNodes = ExtensionNodeList.Empty;
						return childNodes;
					}
				}
				catch (Exception ex) {
					AddinManager.ReportError (null, null, ex, false);
					childNodes = ExtensionNodeList.Empty;
					return childNodes;
				} finally {
					childrenLoaded = true;
				}

				List<ExtensionNode> list = new List<ExtensionNode> ();
				foreach (TreeNode cn in treeNode.Children) {
					
					// For each node check if it is visible for the current context.
					// If something fails while evaluating the condition, just ignore the node.
					
					try {
						if (cn.ExtensionNode != null && cn.IsEnabled)
							list.Add (cn.ExtensionNode);
					} catch (Exception ex) {
						AddinManager.ReportError (null, null, ex, false);
					}
				}
				if (list.Count > 0)
					childNodes = new ExtensionNodeList (list);
				else
					childNodes = ExtensionNodeList.Empty;
			
				return childNodes;
			}
		}
		
		public object[] GetChildObjects ()
		{
			return GetChildObjects (typeof(object), true);
		}
		
		public object[] GetChildObjects (bool reuseCachedInstance)
		{
			return GetChildObjects (typeof(object), reuseCachedInstance);
		}
		
		public object[] GetChildObjects (Type arrayElementType)
		{
			return GetChildObjects (arrayElementType, true);
		}
		
		public T[] GetChildObjects<T> ()
		{
			return (T[]) GetChildObjectsInternal (typeof(T), true);
		}
		
		public object[] GetChildObjects (Type arrayElementType, bool reuseCachedInstance)
		{
			return (object[]) GetChildObjectsInternal (arrayElementType, reuseCachedInstance);
		}
		
		public T[] GetChildObjects<T> (bool reuseCachedInstance)
		{
			return (T[]) GetChildObjectsInternal (typeof(T), reuseCachedInstance);
		}
		
		Array GetChildObjectsInternal (Type arrayElementType, bool reuseCachedInstance)
		{
			ArrayList list = new ArrayList (ChildNodes.Count);
			
			for (int n=0; n<ChildNodes.Count; n++) {
				InstanceExtensionNode node = ChildNodes [n] as InstanceExtensionNode;
				if (node == null) {
					AddinManager.ReportError ("Error while getting object for node in path '" + Path + "'. Extension node is not a subclass of InstanceExtensionNode.", null, null, false);
					continue;
				}
				
				try {
					if (reuseCachedInstance)
						list.Add (node.GetInstance (arrayElementType));
					else
						list.Add (node.CreateInstance (arrayElementType));
				}
				catch (Exception ex) {
					AddinManager.ReportError ("Error while getting object for node in path '" + Path + "'.", node.AddinId, ex, false);
				}
			}
			return list.ToArray (arrayElementType);
		}
		
		internal protected virtual void Read (NodeElement elem)
		{
			if (nodeType == null)
				return;

			NodeAttribute[] attributes = elem.Attributes;
			ReadObject (this, attributes, nodeType.Fields);
			
			if (nodeType.CustomAttributeMember != null) {
				object att = Activator.CreateInstance (nodeType.CustomAttributeMember.MemberType);
				ReadObject (att, attributes, nodeType.CustomAttributeFields);
				nodeType.CustomAttributeMember.SetValue (this, att);
			}
		}
		
		void ReadObject (object ob, NodeAttribute[] attributes, Dictionary<string,ExtensionNodeType.FieldData> fields)
		{
			if (fields == null)
				return;
			
			// Make a copy because we are going to remove fields that have been used
			fields = new Dictionary<string,ExtensionNodeType.FieldData> (fields);
			
			foreach (NodeAttribute at in attributes) {
				
				ExtensionNodeType.FieldData f;
				if (!fields.TryGetValue (at.name, out f))
					continue;
				
				fields.Remove (at.name);
					
				object val;
				Type memberType = f.MemberType;

				if (memberType == typeof(string)) {
					if (f.Localizable)
						val = Addin.Localizer.GetString (at.value);
					else
						val = at.value;
				}
				else if (memberType == typeof(string[])) {
					string[] ss = at.value.Split (',');
					if (ss.Length == 0 && ss[0].Length == 0)
						val = new string [0];
					else {
						for (int n=0; n<ss.Length; n++)
							ss [n] = ss[n].Trim ();
						val = ss;
					}
				}
				else if (memberType.IsEnum) {
					val = Enum.Parse (memberType, at.value);
				}
				else {
					try {
						val = Convert.ChangeType (at.Value, memberType);
					} catch (InvalidCastException) {
						throw new InvalidOperationException ("Property type not supported by [NodeAttribute]: " + f.Member.DeclaringType + "." + f.Member.Name);
					}
				}
				
				f.SetValue (ob, val);
			}
			
			if (fields.Count > 0) {
				// Check if one of the remaining fields is mandatory
				foreach (KeyValuePair<string,ExtensionNodeType.FieldData> e in fields) {
					ExtensionNodeType.FieldData f = e.Value;
					if (f.Required)
						throw new InvalidOperationException ("Required attribute '" + e.Key + "' not found.");
				}
			}
		}
		
		internal bool NotifyChildChanged ()
		{
			if (!childrenLoaded)
				return false;

			ExtensionNodeList oldList = childNodes;
			childrenLoaded = false;
			
			bool changed = false;
			
			foreach (ExtensionNode nod in oldList) {
				if (ChildNodes [nod.Id] == null) {
					changed = true;
					OnChildNodeRemoved (nod);
				}
			}
			foreach (ExtensionNode nod in ChildNodes) {
				if (oldList [nod.Id] == null) {
					changed = true;
					OnChildNodeAdded (nod);
				}
			}
			if (changed)
				OnChildrenChanged ();
			return changed;
		}
		
		// Called when the add-in that defined this extension node is actually
		// loaded in memory.
		internal protected virtual void OnAddinLoaded ()
		{
		}
		
		// Called when the add-in that defined this extension node is being
		// unloaded from memory.
		internal protected virtual void OnAddinUnloaded ()
		{
		}
		
		// Called when the children list of this node has changed. It may be due to add-ins
		// being loaded/unloaded, or to conditions being changed.
		protected virtual void OnChildrenChanged ()
		{
		}
		
		protected virtual void OnChildNodeAdded (ExtensionNode node)
		{
			if (extensionNodeChanged != null)
				extensionNodeChanged (this, new ExtensionNodeEventArgs (ExtensionChange.Add, node));
		}
		
		protected virtual void OnChildNodeRemoved (ExtensionNode node)
		{
			if (extensionNodeChanged != null)
				extensionNodeChanged (this, new ExtensionNodeEventArgs (ExtensionChange.Remove, node));
		}
	}
	
/*	public class ExtensionNode<T>: ExtensionNode where T:CustomExtensionAttribute
	{
		public T Data { get; internal set; }
	}*/
}
