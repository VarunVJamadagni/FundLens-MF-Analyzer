(function () {
    "use strict";


    /* =========================================================
       ELEMENTS
       ========================================================= */

    var searchInput =
        document.getElementById("fl-search-input");

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


    /* =========================================================
       TOP PERFORMING FUNDS
       ========================================================= */

    var topPerformingSection =
        document.getElementById("fl-top-performing");

    var topPerformingLoading =
        document.getElementById("fl-top-performing-loading");

    var topPerformingEmpty =
        document.getElementById("fl-top-performing-empty");

    var topThreeGrid =
        document.getElementById("fl-top-three-grid");

    var topFiveGrid =
        document.getElementById("fl-top-five-grid");

    var topTenGrid =
        document.getElementById("fl-top-ten-grid");


    /* =========================================================
       FILTER BUTTONS
       ========================================================= */

    var filtersApplyBtn =
        document.getElementById("fl-filters-apply");

    var filtersClearBtn =
        document.getElementById("fl-filters-clear");


    /* =========================================================
       FILTER GROUPS
       ========================================================= */

    var filterGroups = {
        plan:
            document.getElementById("fl-filter-plan"),

        option:
            document.getElementById("fl-filter-option"),

        amc:
            document.getElementById("fl-filter-amc"),

        category:
            document.getElementById("fl-filter-category"),

        subcategory:
            document.getElementById("fl-filter-subcategory")
    };


    /* =========================================================
       STATE
       ========================================================= */

    var currentFunds = [];

    var currentPage = 1;

    var pageSize = 15;

    var debounceTimer = null;

    var activeRequestToken = 0;

    var listStateKey =
        "fundLensListState";

    var returnToFundsKey =
        "fundLensReturnToFunds";


    /* =========================================================
       TOP PERFORMING VISIBILITY
       ========================================================= */

    function showTopPerforming() {

        if (!topPerformingSection) {
            return;
        }

        topPerformingSection.hidden = false;
    }


    function hideTopPerforming() {

        if (!topPerformingSection) {
            return;
        }

        topPerformingSection.hidden = true;
    }


    /*
     * The Top Performing section belongs only to the
     * initial landing state.
     *
     * If there is a search query or any selected filter,
     * it must remain hidden.
     */

    function updateTopPerformingVisibility() {

        var searchValue =
            searchInput.value.trim();

        var filterRequest =
            getFilterRequest();

        var filtersSelected =
            hasSelectedFilters(filterRequest);

        if (
            searchValue === "" &&
            !filtersSelected &&
            currentFunds.length === 0
        ) {
            showTopPerforming();
        }
        else {
            hideTopPerforming();
        }
    }


    /* =========================================================
       COMMON UI HELPERS
       ========================================================= */

    function setLoading(isLoading) {

        if (!resultsLoading) {
            return;
        }

        resultsLoading.hidden =
            !isLoading;

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
            await fetch(
                url,
                {
                    method: "GET",

                    headers: {
                        "Accept":
                            "application/json"
                    },

                    cache: "no-store"
                }
            );


        if (!response.ok) {

            throw new Error(
                "Request failed with status " +
                response.status +
                " for " +
                url
            );
        }


        return response.json();
    }


    /* =========================================================
       FILTER OPTIONS
       ========================================================= */

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
                        document.createElement("p");


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


    /* =========================================================
       FILTER REQUEST
       ========================================================= */

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


    /* =========================================================
       APPLY FILTERS
       ========================================================= */

    async function applyFilters() {

        var request =
            getFilterRequest();


        if (!hasSelectedFilters(request)) {

            setStatus(
                "Select at least one filter and click Apply Filters."
            );

            /*
             * No actual filter operation has been performed.
             * Keep the landing state visible.
             */

            updateTopPerformingVisibility();

            return;
        }


        /*
         * User is now doing filter-based searching.
         * Hide Top Performing immediately.
         */

        hideTopPerforming();


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

            var tokenElement =
                document.querySelector(
                    'input[name="__RequestVerificationToken"]'
                );


            var headers = {

                "Accept":
                    "application/json",

                "Content-Type":
                    "application/json"
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

                        body:
                            JSON.stringify(
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


            resultsGrid.innerHTML =
                "";


            resultsEmpty.hidden =
                false;


            resultsCount.textContent =
                "";


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


    /* =========================================================
       SEARCH
       ========================================================= */

    async function runSearch(
        query,
        restoreState
    ) {

        /*
         * Searching means we are no longer on the
         * landing page.
         */

        hideTopPerforming();


        var token =
            ++activeRequestToken;


        currentPage = 1;


        resultsTitle.textContent =
            "Search Results";


        searchClearBtn.hidden =
            false;


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


            rebuildDynamicFilters(
                currentFunds
            );


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


            resultsGrid.innerHTML =
                "";


            resultsEmpty.hidden =
                false;


            resultsCount.textContent =
                "";


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


    /* =========================================================
       FILTERS
       ========================================================= */

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
            String(value).indexOf("-") === 0
        ) {

            return "fl-negative";
        }


        return "fl-positive";
    }


    /* =========================================================
       RESULTS
       ========================================================= */

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


        resultsGrid.innerHTML =
            "";


        if (totalCount === 0) {

            resultsEmpty.hidden =
                false;


            resultsCount.textContent =
                "";


            removePagination();


            return;
        }


        resultsEmpty.hidden =
            true;


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


        if (!element) {
            return;
        }


        element.textContent =
            value || "N/A";


        element.classList.add(
            metricClass(value)
        );
    }


    /* =========================================================
       PAGINATION
       ========================================================= */

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

            resultsSection.scrollIntoView(
                {
                    behavior: "smooth",
                    block: "start"
                }
            );
        }
    }


    /* =========================================================
       DYNAMIC FILTERS
       ========================================================= */

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

                    seen[value] =
                        true;


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


        var previouslyChecked =
            getCheckedValues(
                container
            );


        container.innerHTML =
            "";


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

                        currentPage =
                            1;


                        saveListState();


                        /*
                         * The user has interacted with
                         * filters. Do not show Top
                         * Performing while filters are
                         * selected.
                         *
                         * We do not run the server-side
                         * filter here. The Apply button
                         * remains responsible for that.
                         */

                        if (
                            getFilterRequest()
                        ) {

                            updateTopPerformingVisibility();
                        }


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
                        " " +
                        value
                    )
                );


                container.appendChild(
                    label
                );
            }
        );
    }


    /* =========================================================
       FILTER SEARCH BOXES
       ========================================================= */

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


    function wireStaticFilterGroups() {
        /*
         * Reserved for static filter-group behaviour.
         */
    }


    /* =========================================================
       CLEAR FILTERS
       ========================================================= */

    function clearAllFilters() {

        document
            .querySelectorAll(
                "#fl-filters input[type=checkbox]"
            )
            .forEach(
                function (box) {

                    box.checked =
                        false;
                }
            );


        document
            .querySelectorAll(
                ".fl-filter-search"
            )
            .forEach(
                function (box) {

                    box.value =
                        "";
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


        currentPage =
            1;


        saveListState();


        /*
         * If a search is still active, we remain in
         * search mode and therefore keep Top Performing
         * hidden.
         */

        if (
            searchInput.value.trim() !== ""
        ) {

            hideTopPerforming();
        }
        else {

            /*
             * No search and no filters means we are
             * back on the landing state.
             */

            currentFunds = [];


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


            showTopPerforming();
        }
    }


    /* =========================================================
       SAVE / RESTORE LIST STATE
       ========================================================= */

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

                JSON.stringify(
                    state
                )
            );

        }
        catch (err) {
            /*
             * sessionStorage can fail in private or
             * restricted browser environments.
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


    /* =========================================================
       SEARCH INPUT
       ========================================================= */

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


            /*
             * As soon as the user starts typing,
             * Top Performing should disappear.
             */

            if (value !== "") {

                hideTopPerforming();
            }


            debounceTimer =
                setTimeout(
                    function () {

                        if (value === "") {

                            currentFunds =
                                [];


                            currentPage =
                                1;


                            resultsTitle.textContent =
                                "Search Funds";


                            resultsCount.textContent =
                                "";


                            resultsGrid.innerHTML =
                                "";


                            resultsEmpty.hidden =
                                true;


                            removePagination();


                            loadFilterOptions();


                            setStatus("");


                            /*
                             * Empty search + no selected
                             * filters = landing page.
                             */

                            updateTopPerformingVisibility();


                            try {

                                sessionStorage.removeItem(
                                    listStateKey
                                );

                            }
                            catch (err) {
                            }


                            return;
                        }


                        /*
                         * Non-empty search.
                         */

                        hideTopPerforming();


                        runSearch(
                            value,
                            null
                        );

                    },
                    300
                );
        }
    );


    /* =========================================================
       CLEAR SEARCH
       ========================================================= */

    searchClearBtn.addEventListener(
        "click",

        function () {

            searchInput.value =
                "";


            searchClearBtn.hidden =
                true;


            currentFunds =
                [];


            currentPage =
                1;


            resultsTitle.textContent =
                "Search Funds";


            resultsCount.textContent =
                "";


            resultsGrid.innerHTML =
                "";


            resultsEmpty.hidden =
                true;


            removePagination();


            loadFilterOptions();


            setStatus("");


            try {

                sessionStorage.removeItem(
                    listStateKey
                );

            }
            catch (err) {
            }


            /*
             * Clearing search returns to landing
             * only if no filters are selected.
             */

            updateTopPerformingVisibility();


            searchInput.focus();
        }
    );


    /* =========================================================
       FILTER BUTTONS
       ========================================================= */

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


    /* =========================================================
       TOP PERFORMING FUNDS
       ========================================================= */

    async function loadTopPerformingFunds() {

        if (!topPerformingSection) {
            return;
        }


        try {

            if (topPerformingLoading) {

                topPerformingLoading.hidden =
                    false;
            }


            if (topPerformingEmpty) {

                topPerformingEmpty.hidden =
                    true;
            }


            /*
             * Clear previous cards before loading.
             */

            if (topThreeGrid) {
                topThreeGrid.innerHTML = "";
            }

            if (topFiveGrid) {
                topFiveGrid.innerHTML = "";
            }

            if (topTenGrid) {
                topTenGrid.innerHTML = "";
            }


            /*
             * This is the existing backend endpoint.
             */

            var data =
                await fetchJson(
                    "http://localhost:5001/api/Scheme/top-performing"
                );


            console.log(
                "Top Performing Funds response:",
                data
            );


            if (!data || typeof data !== "object") {

                throw new Error(
                    "Top Performing API returned invalid JSON."
                );
            }


            /*
             * ASP.NET Core normally serializes the
             * properties as camelCase:
             *
             * threeYear
             * fiveYear
             * tenYear
             */

            var threeYear =
                Array.isArray(data.threeYear)
                    ? data.threeYear
                    : [];


            var fiveYear =
                Array.isArray(data.fiveYear)
                    ? data.fiveYear
                    : [];


            var tenYear =
                Array.isArray(data.tenYear)
                    ? data.tenYear
                    : [];


            /*
             * If the server happens to return PascalCase,
             * support that too.
             */

            if (
                threeYear.length === 0 &&
                Array.isArray(data.ThreeYear)
            ) {

                threeYear =
                    data.ThreeYear;
            }


            if (
                fiveYear.length === 0 &&
                Array.isArray(data.FiveYear)
            ) {

                fiveYear =
                    data.FiveYear;
            }


            if (
                tenYear.length === 0 &&
                Array.isArray(data.TenYear)
            ) {

                tenYear =
                    data.TenYear;
            }


            renderTopPerformingPeriod(
                topThreeGrid,
                threeYear
            );


            renderTopPerformingPeriod(
                topFiveGrid,
                fiveYear
            );


            renderTopPerformingPeriod(
                topTenGrid,
                tenYear
            );


            /*
             * Loading succeeded.
             */

            if (topPerformingLoading) {

                topPerformingLoading.hidden =
                    true;
            }


        }
        catch (err) {

            console.error(
                "Failed to load top-performing funds:",
                err
            );


            if (topPerformingLoading) {

                topPerformingLoading.hidden =
                    true;
            }


            if (topPerformingEmpty) {

                topPerformingEmpty.hidden =
                    false;
            }


            if (topThreeGrid) {

                topThreeGrid.innerHTML =
                    "";
            }


            if (topFiveGrid) {

                topFiveGrid.innerHTML =
                    "";
            }


            if (topTenGrid) {

                topTenGrid.innerHTML =
                    "";
            }
        }
    }


    function renderTopPerformingPeriod(
        container,
        funds
    ) {

        if (!container) {
            return;
        }


        container.innerHTML =
            "";


        if (
            !funds ||
            funds.length === 0
        ) {

            var empty =
                document.createElement(
                    "p"
                );


            empty.className =
                "fl-top-performing-period-empty";


            empty.textContent =
                "No qualifying funds available for this period.";


            container.appendChild(
                empty
            );


            return;
        }


        var fragment =
            document.createDocumentFragment();


        funds.forEach(
            function (fund) {

                var card =
                    document.createElement(
                        "a"
                    );


                card.className =
                    "fl-top-fund-card";


                /*
                 * Support normal camelCase response.
                 */

                var schemeCode =
                    fund.schemeCode;


                var schemeName =
                    fund.schemeName;


                var fundHouse =
                    fund.fundHouse;


                var category =
                    fund.category;


                var subCategory =
                    fund.subCategory;


                var cagr =
                    fund.cagr;


                /*
                 * Also support PascalCase response.
                 */

                if (
                    schemeCode === undefined ||
                    schemeCode === null
                ) {

                    schemeCode =
                        fund.SchemeCode;
                }


                if (!schemeName) {

                    schemeName =
                        fund.SchemeName;
                }


                if (!fundHouse) {

                    fundHouse =
                        fund.FundHouse;
                }


                if (!category) {

                    category =
                        fund.Category;
                }


                if (!subCategory) {

                    subCategory =
                        fund.SubCategory;
                }


                if (!cagr) {

                    cagr =
                        fund.CAGR;
                }


                card.href =
                    "/Analytics?schemeCode=" +
                    encodeURIComponent(
                        schemeCode
                    );


                card.setAttribute(
                    "aria-label",

                    "View analytics for " +
                    (
                        schemeName ||
                        "fund"
                    )
                );


                var categoryElement =
                    document.createElement(
                        "span"
                    );


                categoryElement.className =
                    "fl-top-fund-category";


                categoryElement.textContent =
                    category ||
                    "Other";


                var name =
                    document.createElement(
                        "h4"
                    );


                name.className =
                    "fl-top-fund-name";


                name.textContent =
                    schemeName ||
                    "Unknown Fund";


                var subCategoryElement =
                    document.createElement(
                        "p"
                    );


                subCategoryElement.className =
                    "fl-top-fund-subcategory";


                subCategoryElement.textContent =
                    subCategory ||
                    "";


                var details =
                    document.createElement(
                        "div"
                    );


                details.className =
                    "fl-top-fund-details";


                var fundHouseElement =
                    document.createElement(
                        "span"
                    );


                fundHouseElement.className =
                    "fl-top-fund-house";


                fundHouseElement.textContent =
                    fundHouse ||
                    "";


                var cagrElement =
                    document.createElement(
                        "span"
                    );


                cagrElement.className =
                    "fl-top-fund-cagr";


                cagrElement.textContent =
                    cagr ||
                    "N/A";


                details.appendChild(
                    fundHouseElement
                );


                details.appendChild(
                    cagrElement
                );


                card.appendChild(
                    categoryElement
                );


                card.appendChild(
                    name
                );


                card.appendChild(
                    subCategoryElement
                );


                card.appendChild(
                    details
                );


                fragment.appendChild(
                    card
                );
            }
        );


        container.appendChild(
            fragment
        );
    }


    /* =========================================================
       INITIALISE
       ========================================================= */

    wireFilterSearchBoxes();

    wireStaticFilterGroups();


    /*
     * The page initially has:
     *
     * - no search
     * - no selected filters
     * - no search results
     *
     * Therefore this is the landing state.
     */

    showTopPerforming();


    loadFilterOptions();


    /*
     * Load Top Performing once.
     *
     * It stays in the DOM but visibility is controlled
     * separately by updateTopPerformingVisibility().
     */

    loadTopPerformingFunds();


    /* =========================================================
       RESTORE FROM ANALYTICS
       ========================================================= */

    var shouldRestore =
        false;


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

        shouldRestore =
            false;
    }


    if (shouldRestore) {

        var savedState =
            getSavedListState();


        if (
            savedState &&
            savedState.query
        ) {

            /*
             * Returning to an existing search means
             * this is NOT the landing page.
             */

            hideTopPerforming();


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