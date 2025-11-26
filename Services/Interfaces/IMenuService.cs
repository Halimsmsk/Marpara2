namespace Morpara.Services.Interfaces
{
    public interface IMenuService
    {
        /// <summary>
        /// Gets static menu items for navigation
        /// </summary>
        object GetStaticMenuItems(string id);

        /// <summary>
        /// Gets static about menu items
        /// </summary>
        object GetStaticAbMenuItems(string id);
    }
}
