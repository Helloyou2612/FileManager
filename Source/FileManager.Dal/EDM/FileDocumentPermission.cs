//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace FileManager.Dal.EDM
{
    using System;
    using System.Collections.Generic;
    
    public partial class FileDocumentPermission
    {
        public int Id { get; set; }
        public int DepartmentId_PK { get; set; }
        public int FileDocumentId_PK { get; set; }
        public bool Rename { get; set; }
        public bool Move { get; set; }
        public bool Copy { get; set; }
        public bool Delete { get; set; }
        public bool Download { get; set; }
        public bool Create { get; set; }
        public bool Upload { get; set; }
        public bool MoveOrCopyInto { get; set; }
        public Nullable<bool> IsFolder { get; set; }
    }
}
