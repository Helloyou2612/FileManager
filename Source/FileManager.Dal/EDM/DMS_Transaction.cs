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
    
    public partial class DMS_Transaction
    {
        public int ID { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string ObjectId { get; set; }
        public string ObjectName { get; set; }
        public string ObjectType { get; set; }
        public string Action { get; set; }
        public string Description { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public string LocalIp { get; set; }
        public string UserIp { get; set; }
    }
}
