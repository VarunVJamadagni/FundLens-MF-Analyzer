(function () {
    "use strict";

    var dropdowns =
        document.querySelectorAll(".fl-nav-dropdown");

    if (!dropdowns.length) {
        return;
    }

    function closeAllDropdowns(except) {

        dropdowns.forEach(function (dropdown) {

            if (dropdown !== except) {
                dropdown.classList.remove("is-open");

                var button =
                    dropdown.querySelector(
                        ".fl-nav-dropdown-toggle"
                    );

                if (button) {
                    button.setAttribute(
                        "aria-expanded",
                        "false"
                    );
                }
            }
        });
    }


    dropdowns.forEach(function (dropdown) {

        var button =
            dropdown.querySelector(
                ".fl-nav-dropdown-toggle"
            );

        if (!button) {
            return;
        }

        button.setAttribute(
            "aria-expanded",
            "false"
        );

        button.addEventListener(
            "click",
            function (event) {

                event.preventDefault();
                event.stopPropagation();

                var isOpen =
                    dropdown.classList.contains(
                        "is-open"
                    );

                closeAllDropdowns(dropdown);

                if (isOpen) {
                    dropdown.classList.remove(
                        "is-open"
                    );

                    button.setAttribute(
                        "aria-expanded",
                        "false"
                    );
                }
                else {
                    dropdown.classList.add(
                        "is-open"
                    );

                    button.setAttribute(
                        "aria-expanded",
                        "true"
                    );
                }
            }
        );
    });


    /*
     * Close dropdown when clicking outside
     */
    document.addEventListener(
        "click",
        function (event) {

            if (
                !event.target.closest(
                    ".fl-nav-dropdown"
                )
            ) {
                closeAllDropdowns(null);
            }
        }
    );


    /*
     * Close dropdown with Escape
     */
    document.addEventListener(
        "keydown",
        function (event) {

            if (event.key === "Escape") {

                closeAllDropdowns(null);

                var openButton =
                    document.querySelector(
                        ".fl-nav-dropdown.is-open " +
                        ".fl-nav-dropdown-toggle"
                    );

                if (openButton) {
                    openButton.focus();
                }
            }
        }
    );

})();