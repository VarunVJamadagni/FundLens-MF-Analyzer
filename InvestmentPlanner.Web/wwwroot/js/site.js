(function () {
    "use strict";

    var searchInput =
        document.getElementById("fl-search-input");

    /*
     * This file is loaded on every page.
     * Only run the fund-list logic when the search input exists.
     */
    if (!searchInput) {
        return;
    }

    var searchClearBtn =
        document.getElementById("fl-search-clear");

    var searchStatus =
        document.getElementById("fl-search-status");

    var resultsTitle =
        document.getElementById("fl-results-title");

    var resultsCount =
        document.getElementById("fl-results-count");

    var resultsGrid =
        document.getElementById("fl-results-grid");

    var resultsEmpty =
        document.getElementById("fl-results-empty");

    var resultsLoading =
        document.getElementById("fl-results-loading");

    var cardTemplate =
        document.getElementById("fl-fund-card-template");

    var filtersApplyBtn =
        document.getElementById("fl-filters-apply");

    var filtersClearBtn =
        document.getElementById("fl-filters-clear");

    var filterGroups = {
        plan: document.getElementById("fl-filter-plan"),
        option: document.getElementById("fl-filter-option"),
        amc: document.getElementById("fl-filter-amc"),
        category: document.getElementById("fl-filter-category"),
        subcategory: document.getElementById("fl-filter-subcategory")
    };


    /*
     * ---------------------------------------------------------
     * STATE
     * ---------------------------------------------------------
     */

    /*
     * All funds returned by the current search/filter operation.
     */
    var currentFunds = [];

    /*
     * Current page for client-side pagination.
     */
    var currentPage = 1;

    /*
     * Number of funds displayed per page.
     */
    var pageSize = 15;

    var debounceTimer = null;

    var activeRequestToken = 0;

    /*
     * Session storage key used to restore the user's
     * search/filter/page state after opening a fund.
     */
    var listStateKey =
        "fundLensListState";

    var returnToFundsKey =
        "fundLensReturnToFunds";


    /*
     * ---------------------------------------------------------
     * COMMON UI HELPERS
     * ---------------------------------------------------------
     */

    function setLoading(isLoading) {
        if (!resultsLoading) {
            return;
        }

        resultsLoading.hidden = !isLoading;

        if (isLoading) {
            resultsGrid.innerHTML = "";

            resultsEmpty.hidden = true;
        }
    }


    function setStatus(text) {
        if (searchStatus) {
            searchStatus.textContent =
                text || "";
        }
    }


    async function fetchJson(url) {
        var response =
            await fetch(url, {
                headers: {
                    "Accept": "application/json"
                }
            });

        if (!response.ok) {
            throw new Error(
                "Request failed with status " +
                response.status
            );
        }

        return response.json();
    }


    /*
     * ---------------------------------------------------------
     * FILTER OPTIONS
     * ---------------------------------------------------------
     *
     * Loaded from:
     *
     *     GET ?handler=FilterOptions
     *
     * This is used by the landing-page filter UI.
     */

    async function loadFilterOptions() {
        try {
            var data =
                await fetchJson(
                    "?handler=FilterOptions"
                );

            buildCheckboxOptions(
                filterGroups.plan,

                Array.isArray(data.plans)
                    ? data.plans
                    : [],

                "No plan data available."
            );

            buildCheckboxOptions(
                filterGroups.option,

                Array.isArray(data.options)
                    ? data.options
                    : [],

                "No option data available."
            );

            buildCheckboxOptions(
                filterGroups.amc,

                Array.isArray(data.fundHouses)
                    ? data.fundHouses
                    : [],

                "No AMC data available."
            );

            buildCheckboxOptions(
                filterGroups.category,

                Array.isArray(data.categories)
                    ? data.categories
                    : [],

                "No category data available."
            );

            buildCheckboxOptions(
                filterGroups.subcategory,

                Array.isArray(data.subCategories)
                    ? data.subCategories
                    : [],

                "No sub-category data available."
            );
        }
        catch (err) {
            console.error(
                "Failed to load filter options:",
                err
            );

            Object.keys(filterGroups).forEach(
                function (key) {
                    var container =
                        filterGroups[key];

                    if (!container) {
                        return;
                    }

                    container.innerHTML = "";

                    var empty =
                        document.createElement(
                            "p"
                        );

                    empty.className =
                        "fl-filter-empty";

                    empty.textContent =
                        "Unable to load filter options.";

                    container.appendChild(
                        empty
                    );
                }
            );

            setStatus(
                "Unable to load filters. Please refresh the page."
            );
        }
    }


    /*
     * ---------------------------------------------------------
     * FILTER REQUEST
     * ---------------------------------------------------------
     */

    function getFilterRequest() {
        return {
            fundHouses:
                getCheckedValues(
                    filterGroups.amc
                ),

            categories:
                getCheckedValues(
                    filterGroups.category
                ),

            subCategories:
                getCheckedValues(
                    filterGroups.subcategory
                ),

            plans:
                getCheckedValues(
                    filterGroups.plan
                ),

            options:
                getCheckedValues(
                    filterGroups.option
                )
        };
    }


    function hasSelectedFilters(request) {
        return (
            request.fundHouses.length > 0 ||
            request.categories.length > 0 ||
            request.subCategories.length > 0 ||
            request.plans.length > 0 ||
            request.options.length > 0
        );
    }


    /*
     * ---------------------------------------------------------
     * APPLY FILTERS
     * ---------------------------------------------------------
     *
     * Landing page:
     *
     *     Select filters
     *          ↓
     *     No API request
     *          ↓
     *     Click Apply Filters
     *          ↓
     *     POST ?handler=Filter
     */

    async function applyFilters() {
        var request =
            getFilterRequest();

        if (!hasSelectedFilters(request)) {
            setStatus(
                "Select at least one filter and click Apply Filters."
            );

            return;
        }

        var token =
            ++activeRequestToken;

        currentPage = 1;

        resultsTitle.textContent =
            "Filtered Funds";

        setLoading(true);

        setStatus(
            "Applying filters..."
        );

        try {
            /*
             * Razor Pages anti-forgery token.
             *
             * Index.cshtml contains:
             *
             * @Html.AntiForgeryToken()
             *
             * We send that token in the request header.
             */
            var tokenElement =
                document.querySelector(
                    'input[name="__RequestVerificationToken"]'
                );

            var headers = {
                "Accept": "application/json",
                "Content-Type": "application/json"
            };

            if (tokenElement) {
                headers["RequestVerificationToken"] =
                    tokenElement.value;
            }

            var response =
                await fetch(
                    "?handler=Filter",
                    {
                        method: "POST",

                        headers: headers,

                        body: JSON.stringify(
                            request
                        )
                    }
                );

            if (!response.ok) {
                throw new Error(
                    "Request failed with status " +
                    response.status
                );
            }

            var data =
                await response.json();

            if (token !== activeRequestToken) {
                return;
            }

            currentFunds =
                Array.isArray(data)
                    ? data
                    : [];

            if (currentFunds.length === 0) {
                setStatus(
                    "No funds matched the selected filters."
                );
            }
            else {
                setStatus("");
            }

            saveListState();

            renderResults();
        }
        catch (err) {
            if (token !== activeRequestToken) {
                return;
            }

            console.error(
                "Failed to apply filters:",
                err
            );

            currentFunds = [];

            resultsGrid.innerHTML = "";

            resultsEmpty.hidden = false;

            resultsCount.textContent = "";

            removePagination();

            setStatus(
                "Filtering failed. Please try again."
            );
        }
        finally {
            if (token === activeRequestToken) {
                setLoading(false);
            }
        }
    }


    /*
     * ---------------------------------------------------------
     * SEARCH
     * ---------------------------------------------------------
     */

    async function runSearch(
        query,
        restoreState
    ) {
        var token =
            ++activeRequestToken;

        currentPage = 1;

        resultsTitle.textContent =
            "Search Results";

        searchClearBtn.hidden = false;

        setLoading(true);

        setStatus(
            "Searching..."
        );

        try {
            var data =
                await fetchJson(
                    "?handler=Search&query=" +
                    encodeURIComponent(query)
                );

            if (token !== activeRequestToken) {
                return;
            }

            currentFunds =
                Array.isArray(data)
                    ? data
                    : [];

            /*
             * Search results rebuild:
             *
             * AMC
             * Category
             * Sub-Category
             *
             * Plan and Option remain based on the
             * complete catalogue.
             */
            rebuildDynamicFilters(
                currentFunds
            );

            /*
             * Restore filters and page only when returning
             * from an analytics page.
             */
            if (restoreState) {
                applySavedFilterState(
                    restoreState
                );

                currentPage =
                    Number(
                        restoreState.page
                    ) || 1;

                if (currentPage < 1) {
                    currentPage = 1;
                }

                var totalPages =
                    Math.max(
                        1,
                        Math.ceil(
                            getFilteredFunds().length /
                            pageSize
                        )
                    );

                if (currentPage > totalPages) {
                    currentPage =
                        totalPages;
                }
            }

            if (currentFunds.length === 0) {
                setStatus(
                    "No funds matched your search."
                );
            }
            else {
                setStatus("");
            }

            renderResults();
        }
        catch (err) {
            if (token !== activeRequestToken) {
                return;
            }

            console.error(
                "Search failed:",
                err
            );

            currentFunds = [];

            rebuildDynamicFilters(
                currentFunds
            );

            resultsGrid.innerHTML = "";

            resultsEmpty.hidden = false;

            resultsCount.textContent = "";

            removePagination();

            setStatus(
                "Search failed. Please try again."
            );
        }
        finally {
            if (token === activeRequestToken) {
                setLoading(false);
            }
        }
    }


    /*
     * ---------------------------------------------------------
     * FILTERS
     * ---------------------------------------------------------
     */

    function getCheckedValues(
        container
    ) {
        if (!container) {
            return [];
        }

        var boxes =
            container.querySelectorAll(
                "input[type=checkbox]:checked"
            );

        return Array.prototype.map.call(
            boxes,
            function (box) {
                return box.value;
            }
        );
    }


    function fundMatchesFilters(
        fund
    ) {
        var planChecked =
            getCheckedValues(
                filterGroups.plan
            );

        var optionChecked =
            getCheckedValues(
                filterGroups.option
            );

        var amcChecked =
            getCheckedValues(
                filterGroups.amc
            );

        var categoryChecked =
            getCheckedValues(
                filterGroups.category
            );

        var subCategoryChecked =
            getCheckedValues(
                filterGroups.subcategory
            );


        if (
            planChecked.length &&
            planChecked.indexOf(
                fund.plan
            ) === -1
        ) {
            return false;
        }


        if (
            optionChecked.length &&
            optionChecked.indexOf(
                fund.option
            ) === -1
        ) {
            return false;
        }


        if (
            amcChecked.length &&
            amcChecked.indexOf(
                fund.fundHouse
            ) === -1
        ) {
            return false;
        }


        if (
            categoryChecked.length &&
            categoryChecked.indexOf(
                fund.schemeCategory
            ) === -1
        ) {
            return false;
        }


        if (
            subCategoryChecked.length &&
            subCategoryChecked.indexOf(
                fund.schemeSubCategory
            ) === -1
        ) {
            return false;
        }


        return true;
    }


    function getFilteredFunds() {
        return currentFunds.filter(
            fundMatchesFilters
        );
    }


    function metricClass(value) {
        if (
            !value ||
            value === "N/A"
        ) {
            return "fl-na";
        }

        if (
            value.indexOf("-") === 0
        ) {
            return "fl-negative";
        }

        return "fl-positive";
    }


    /*
     * ---------------------------------------------------------
     * RESULTS
     * ---------------------------------------------------------
     */

    function renderResults() {
        var filtered =
            getFilteredFunds();

        var totalCount =
            filtered.length;

        var totalPages =
            Math.max(
                1,
                Math.ceil(
                    totalCount /
                    pageSize
                )
            );


        if (currentPage > totalPages) {
            currentPage =
                totalPages;
        }


        resultsGrid.innerHTML = "";


        if (totalCount === 0) {
            resultsEmpty.hidden = false;

            resultsCount.textContent = "";

            removePagination();

            return;
        }


        resultsEmpty.hidden = true;


        var startIndex =
            (currentPage - 1) *
            pageSize;


        var endIndex =
            Math.min(
                startIndex + pageSize,
                totalCount
            );


        resultsCount.textContent =
            "Showing " +
            (startIndex + 1) +
            "–" +
            endIndex +
            " of " +
            totalCount +
            (
                totalCount === 1
                    ? " fund"
                    : " funds"
            );


        var pageFunds =
            filtered.slice(
                startIndex,
                endIndex
            );


        var fragment =
            document.createDocumentFragment();


        pageFunds.forEach(
            function (fund) {
                var node =
                    cardTemplate.content.cloneNode(
                        true
                    );


                var anchor =
                    node.querySelector(
                        ".fl-fund-card"
                    );


                anchor.href =
                    "/Analytics?schemeCode=" +
                    encodeURIComponent(
                        fund.schemeCode
                    );


                /*
                 * Save list state before opening
                 * the analytics page.
                 */
                anchor.addEventListener(
                    "click",
                    function () {
                        saveListState();
                    }
                );


                node.querySelector(
                    ".fl-fund-name"
                ).textContent =
                    fund.schemeName ||
                    "Unknown Fund";


                node.querySelector(
                    ".fl-fund-plan"
                ).textContent =
                    fund.plan ||
                    "Standard";


                node.querySelector(
                    ".fl-fund-nav-value"
                ).textContent =
                    "₹" +
                    Number(
                        fund.currentNAV || 0
                    ).toFixed(4);


                node.querySelector(
                    ".fl-fund-amc"
                ).textContent =
                    fund.fundHouse ||
                    "";


                setMetric(
                    node,
                    ".fl-m1",
                    fund.return1Month
                );


                setMetric(
                    node,
                    ".fl-m3",
                    fund.return3Month
                );


                setMetric(
                    node,
                    ".fl-m6",
                    fund.return6Month
                );


                setMetric(
                    node,
                    ".fl-y1",
                    fund.cagr1Year
                );


                setMetric(
                    node,
                    ".fl-y3",
                    fund.cagr3Year
                );


                setMetric(
                    node,
                    ".fl-y5",
                    fund.cagr5Year
                );


                setMetric(
                    node,
                    ".fl-y10",
                    fund.cagr10Year
                );


                fragment.appendChild(
                    node
                );
            }
        );


        resultsGrid.appendChild(
            fragment
        );


        renderPagination(
            totalPages
        );
    }


    function setMetric(
        node,
        selector,
        value
    ) {
        var element =
            node.querySelector(
                selector
            );

        element.textContent =
            value || "N/A";

        element.classList.add(
            metricClass(value)
        );
    }


    /*
     * ---------------------------------------------------------
     * PAGINATION
     * ---------------------------------------------------------
     */

    function renderPagination(
        totalPages
    ) {
        removePagination();

        if (totalPages <= 1) {
            return;
        }


        var pagination =
            document.createElement(
                "div"
            );

        pagination.id =
            "fl-pagination";

        pagination.className =
            "fl-pagination";


        /*
         * Previous button
         */
        var previousButton =
            document.createElement(
                "button"
            );

        previousButton.type =
            "button";

        previousButton.className =
            "fl-pagination-button " +
            "fl-pagination-prev";

        previousButton.innerHTML =
            '<span aria-hidden="true">←</span>' +
            '<span>Previous</span>';

        previousButton.disabled =
            currentPage === 1;

        previousButton.setAttribute(
            "aria-label",
            "Go to previous page"
        );


        previousButton.addEventListener(
            "click",
            function () {
                if (currentPage > 1) {
                    currentPage--;

                    saveListState();

                    renderResults();

                    scrollToResults();
                }
            }
        );


        pagination.appendChild(
            previousButton
        );


        /*
         * Page numbers
         */
        var pages =
            getPageNumbers(
                totalPages
            );


        pages.forEach(
            function (page) {
                if (page === "...") {
                    var ellipsis =
                        document.createElement(
                            "span"
                        );

                    ellipsis.className =
                        "fl-pagination-ellipsis";

                    ellipsis.textContent =
                        "…";

                    pagination.appendChild(
                        ellipsis
                    );

                    return;
                }


                var pageButton =
                    document.createElement(
                        "button"
                    );

                pageButton.type =
                    "button";

                pageButton.className =
                    "fl-pagination-button" +
                    (
                        page === currentPage
                            ? " active"
                            : ""
                    );

                pageButton.textContent =
                    page;


                pageButton.setAttribute(
                    "aria-label",
                    "Go to page " +
                    page
                );


                if (
                    page === currentPage
                ) {
                    pageButton.setAttribute(
                        "aria-current",
                        "page"
                    );
                }


                pageButton.addEventListener(
                    "click",
                    function () {
                        currentPage =
                            page;

                        saveListState();

                        renderResults();

                        scrollToResults();
                    }
                );


                pagination.appendChild(
                    pageButton
                );
            }
        );


        /*
         * Next button
         */
        var nextButton =
            document.createElement(
                "button"
            );

        nextButton.type =
            "button";

        nextButton.className =
            "fl-pagination-button " +
            "fl-pagination-next";

        nextButton.innerHTML =
            '<span>Next</span>' +
            '<span aria-hidden="true">→</span>';

        nextButton.disabled =
            currentPage === totalPages;

        nextButton.setAttribute(
            "aria-label",
            "Go to next page"
        );


        nextButton.addEventListener(
            "click",
            function () {
                if (
                    currentPage <
                    totalPages
                ) {
                    currentPage++;

                    saveListState();

                    renderResults();

                    scrollToResults();
                }
            }
        );


        pagination.appendChild(
            nextButton
        );


        var resultsSection =
            document.querySelector(
                ".fl-results"
            );


        if (resultsSection) {
            resultsSection.appendChild(
                pagination
            );
        }
    }


    function getPageNumbers(
        totalPages
    ) {
        var pages = [];


        if (totalPages <= 7) {
            for (
                var i = 1;
                i <= totalPages;
                i++
            ) {
                pages.push(i);
            }

            return pages;
        }


        pages.push(1);


        if (currentPage > 4) {
            pages.push("...");
        }


        var start =
            Math.max(
                2,
                currentPage - 1
            );


        var end =
            Math.min(
                totalPages - 1,
                currentPage + 1
            );


        for (
            var page = start;
            page <= end;
            page++
        ) {
            pages.push(page);
        }


        if (
            currentPage <
            totalPages - 3
        ) {
            pages.push("...");
        }


        pages.push(
            totalPages
        );


        return pages;
    }


    function removePagination() {
        var existing =
            document.getElementById(
                "fl-pagination"
            );

        if (existing) {
            existing.remove();
        }
    }


    function scrollToResults() {
        var resultsSection =
            document.querySelector(
                ".fl-results"
            );

        if (resultsSection) {
            resultsSection.scrollIntoView({
                behavior: "smooth",
                block: "start"
            });
        }
    }


    /*
     * ---------------------------------------------------------
     * DYNAMIC FILTERS
     * ---------------------------------------------------------
     *
     * During a search:
     *
     * AMC
     * Category
     * Sub-Category
     *
     * are rebuilt from the search results.
     *
     * Plan and Option remain populated from the
     * complete catalogue.
     */

    function rebuildDynamicFilters(
        funds
    ) {
        buildCheckboxOptions(
            filterGroups.amc,

            distinctValues(
                funds,
                "fundHouse"
            ),

            "No AMC data available."
        );


        buildCheckboxOptions(
            filterGroups.category,

            distinctValues(
                funds,
                "schemeCategory"
            ),

            "No category data available."
        );


        buildCheckboxOptions(
            filterGroups.subcategory,

            distinctValues(
                funds,
                "schemeSubCategory"
            ),

            "No sub-category data available."
        );
    }


    function distinctValues(
        funds,
        propertyName
    ) {
        var seen = {};

        var values = [];


        funds.forEach(
            function (fund) {
                var value =
                    fund[propertyName];


                if (
                    value &&
                    !seen[value]
                ) {
                    seen[value] = true;

                    values.push(
                        value
                    );
                }
            }
        );


        values.sort(
            function (a, b) {
                return a.localeCompare(
                    b,
                    undefined,
                    {
                        sensitivity:
                            "base"
                    }
                );
            }
        );


        return values;
    }


    function buildCheckboxOptions(
        container,
        values,
        emptyMessage
    ) {
        if (!container) {
            return;
        }


        /*
         * Keep existing selections where possible.
         */
        var previouslyChecked =
            getCheckedValues(
                container
            );


        container.innerHTML = "";


        if (
            !values ||
            values.length === 0
        ) {
            var empty =
                document.createElement(
                    "p"
                );

            empty.className =
                "fl-filter-empty";

            empty.textContent =
                emptyMessage;

            container.appendChild(
                empty
            );

            return;
        }


        values.forEach(
            function (value) {
                var label =
                    document.createElement(
                        "label"
                    );


                var input =
                    document.createElement(
                        "input"
                    );


                input.type =
                    "checkbox";


                input.value =
                    value;


                if (
                    previouslyChecked.indexOf(
                        value
                    ) !== -1
                ) {
                    input.checked =
                        true;
                }


                input.addEventListener(
                    "change",
                    function () {
                        currentPage = 1;

                        saveListState();


                        /*
                         * Landing page:
                         *
                         *     checkbox
                         *          ↓
                         *     no API request
                         *
                         * Search/filter results:
                         *
                         *     checkbox
                         *          ↓
                         *     immediate client filtering
                         */
                        if (
                            currentFunds.length > 0 ||
                            searchInput.value.trim() !== ""
                        ) {
                            renderResults();
                        }
                    }
                );


                label.appendChild(
                    input
                );


                label.appendChild(
                    document.createTextNode(
                        " " + value
                    )
                );


                container.appendChild(
                    label
                );
            }
        );
    }


    /*
     * ---------------------------------------------------------
     * FILTER SEARCH BOXES
     * ---------------------------------------------------------
     */

    function wireFilterSearchBoxes() {
        var searchBoxes =
            document.querySelectorAll(
                ".fl-filter-search"
            );


        searchBoxes.forEach(
            function (box) {
                box.addEventListener(
                    "input",
                    function () {
                        var targetContainer =
                            document.getElementById(
                                box.getAttribute(
                                    "data-target"
                                )
                            );


                        if (!targetContainer) {
                            return;
                        }


                        var term =
                            box.value
                                .trim()
                                .toLowerCase();


                        var labels =
                            targetContainer.querySelectorAll(
                                "label"
                            );


                        labels.forEach(
                            function (label) {
                                var text =
                                    label.textContent
                                        .trim()
                                        .toLowerCase();


                                label.style.display =
                                    term === "" ||
                                    text.indexOf(
                                        term
                                    ) !== -1
                                        ? ""
                                        : "none";
                            }
                        );
                    }
                );
            }
        );
    }


    /*
     * Plan and Option are now populated dynamically.
     */
    function wireStaticFilterGroups() {
        /*
         * Intentionally empty.
         */
    }


    /*
     * ---------------------------------------------------------
     * CLEAR FILTERS
     * ---------------------------------------------------------
     */

    function clearAllFilters() {
        document
            .querySelectorAll(
                "#fl-filters input[type=checkbox]"
            )
            .forEach(
                function (box) {
                    box.checked = false;
                }
            );


        document
            .querySelectorAll(
                ".fl-filter-search"
            )
            .forEach(
                function (box) {
                    box.value = "";
                }
            );


        document
            .querySelectorAll(
                "#fl-filters label"
            )
            .forEach(
                function (label) {
                    label.style.display =
                        "";
                }
            );


        currentPage = 1;

        saveListState();


        /*
         * If results have already been loaded,
         * show all loaded results.
         *
         * On the empty landing page, don't call
         * the API and don't render anything.
         */
        if (currentFunds.length > 0) {
            renderResults();
        }
        else {
            resultsGrid.innerHTML =
                "";

            resultsCount.textContent =
                "";

            resultsEmpty.hidden =
                true;

            removePagination();

            resultsTitle.textContent =
                "Search Funds";

            setStatus("");
        }
    }


    /*
     * ---------------------------------------------------------
     * SAVE / RESTORE LIST STATE
     * ---------------------------------------------------------
     */

    function saveListState() {
        var state = {
            query:
                searchInput.value.trim(),

            page:
                currentPage,

            filters: {
                plan:
                    getCheckedValues(
                        filterGroups.plan
                    ),

                option:
                    getCheckedValues(
                        filterGroups.option
                    ),

                amc:
                    getCheckedValues(
                        filterGroups.amc
                    ),

                category:
                    getCheckedValues(
                        filterGroups.category
                    ),

                subcategory:
                    getCheckedValues(
                        filterGroups.subcategory
                    )
            }
        };


        try {
            sessionStorage.setItem(
                listStateKey,
                JSON.stringify(state)
            );
        }
        catch (err) {
            /*
             * Session storage is only an enhancement.
             * Application continues to work if unavailable.
             */
        }
    }


    function getSavedListState() {
        try {
            var raw =
                sessionStorage.getItem(
                    listStateKey
                );


            if (!raw) {
                return null;
            }


            return JSON.parse(
                raw
            );
        }
        catch (err) {
            return null;
        }
    }


    function applySavedFilterState(
        state
    ) {
        if (
            !state ||
            !state.filters
        ) {
            return;
        }


        setCheckedValues(
            filterGroups.plan,
            state.filters.plan
        );


        setCheckedValues(
            filterGroups.option,
            state.filters.option
        );


        setCheckedValues(
            filterGroups.amc,
            state.filters.amc
        );


        setCheckedValues(
            filterGroups.category,
            state.filters.category
        );


        setCheckedValues(
            filterGroups.subcategory,
            state.filters.subcategory
        );
    }


    function setCheckedValues(
        container,
        values
    ) {
        if (
            !container ||
            !Array.isArray(values)
        ) {
            return;
        }


        container
            .querySelectorAll(
                "input[type=checkbox]"
            )
            .forEach(
                function (box) {
                    box.checked =
                        values.indexOf(
                            box.value
                        ) !== -1;
                }
            );
    }


    /*
     * ---------------------------------------------------------
     * SEARCH INPUT
     * ---------------------------------------------------------
     */

    searchInput.addEventListener(
        "input",
        function () {
            var value =
                searchInput.value.trim();


            searchClearBtn.hidden =
                value === "";


            if (debounceTimer) {
                clearTimeout(
                    debounceTimer
                );
            }


            debounceTimer =
                setTimeout(
                    function () {
                        /*
                         * Empty search:
                         *
                         * Return to landing-page
                         * filter mode.
                         */
                        if (value === "") {
                            currentFunds = [];

                            currentPage = 1;


                            resultsTitle.textContent =
                                "Search Funds";


                            resultsCount.textContent =
                                "";


                            resultsGrid.innerHTML =
                                "";


                            resultsEmpty.hidden =
                                true;


                            removePagination();


                            /*
                             * Reload complete filter
                             * catalogue.
                             */
                            loadFilterOptions();


                            setStatus("");


                            try {
                                sessionStorage.removeItem(
                                    listStateKey
                                );
                            }
                            catch (err) {
                                // Ignore storage errors.
                            }


                            return;
                        }


                        runSearch(
                            value,
                            null
                        );
                    },
                    300
                );
        }
    );


    /*
     * ---------------------------------------------------------
     * CLEAR SEARCH
     * ---------------------------------------------------------
     */

    searchClearBtn.addEventListener(
        "click",
        function () {
            searchInput.value =
                "";

            searchClearBtn.hidden =
                true;


            currentFunds = [];

            currentPage = 1;


            resultsTitle.textContent =
                "Search Funds";


            resultsCount.textContent =
                "";


            resultsGrid.innerHTML =
                "";


            resultsEmpty.hidden =
                true;


            removePagination();


            /*
             * Return filter groups to
             * complete catalogue.
             */
            loadFilterOptions();


            setStatus("");


            try {
                sessionStorage.removeItem(
                    listStateKey
                );
            }
            catch (err) {
                // Ignore storage errors.
            }


            searchInput.focus();
        }
    );


    /*
     * ---------------------------------------------------------
     * FILTER BUTTONS
     * ---------------------------------------------------------
     */

    if (filtersApplyBtn) {
        filtersApplyBtn.addEventListener(
            "click",
            applyFilters
        );
    }


    if (filtersClearBtn) {
        filtersClearBtn.addEventListener(
            "click",
            clearAllFilters
        );
    }


    /*
     * ---------------------------------------------------------
     * INITIALISE
     * ---------------------------------------------------------
     */

    wireFilterSearchBoxes();

    wireStaticFilterGroups();


    /*
     * Populate all filter groups from the
     * full AMFI catalogue.
     */
    loadFilterOptions();


    /*
     * ---------------------------------------------------------
     * RESTORE FROM ANALYTICS
     * ---------------------------------------------------------
     */

    var shouldRestore = false;


    try {
        shouldRestore =
            sessionStorage.getItem(
                returnToFundsKey
            ) === "1";


        if (shouldRestore) {
            sessionStorage.removeItem(
                returnToFundsKey
            );
        }
    }
    catch (err) {
        shouldRestore = false;
    }


    if (shouldRestore) {
        var savedState =
            getSavedListState();


        if (
            savedState &&
            savedState.query
        ) {
            searchInput.value =
                savedState.query;


            searchClearBtn.hidden =
                false;


            runSearch(
                savedState.query,
                savedState
            );
        }
    }

})();