// Scaffold генерирует partial-классы, поэтому к ним можно «дописать» интерфейс в отдельном файле.
// Повторный scaffold с --force этот файл не трогает.
// Интерфейс нужен, чтобы простые справочники (только Id и Name) обслуживал один общий код.

namespace Clinic.Entities
{
    public interface INamedEntity
    {
        int Id { get; set; }
        string Name { get; set; }
    }
}

namespace Clinic.Entities
{
    public partial class Post : Clinic.Entities.INamedEntity { }
    public partial class Specialty : Clinic.Entities.INamedEntity { }
    public partial class ServiceGroup : Clinic.Entities.INamedEntity { }
    public partial class Drug : Clinic.Entities.INamedEntity { }
    public partial class DocumentType : Clinic.Entities.INamedEntity { }
}
