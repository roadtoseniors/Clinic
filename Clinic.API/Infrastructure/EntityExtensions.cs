// Scaffold генерирует partial-классы, поэтому к ним можно «дописать» интерфейс в отдельном файле.
// Повторный scaffold с --force этот файл не трогает.
// Интерфейс нужен, чтобы простые справочники (только Id и Name) обслуживал один общий код.

namespace Clinic.API
{
    public interface INamedEntity
    {
        int Id { get; set; }
        string Name { get; set; }
    }
}

namespace Clinic.Entities
{
    public partial class Post : Clinic.API.INamedEntity { }
    public partial class Specialty : Clinic.API.INamedEntity { }
    public partial class ServiceGroup : Clinic.API.INamedEntity { }
    public partial class Drug : Clinic.API.INamedEntity { }
    public partial class DocumentType : Clinic.API.INamedEntity { }
}
